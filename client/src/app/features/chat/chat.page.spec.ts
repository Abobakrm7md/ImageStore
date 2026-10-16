import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ChatPage } from './chat.page';
import { ChatService, Conversation, Message, SendRequest } from '../../core/chat/chat.service';
import { AuthService } from '../../core/auth/auth.service';
import { SocialApiService } from '../../core/social/social-api.service';

const conversation: Conversation = {
  id: 'conversation',
  participants: [
    { id: 'me', userName: 'me', displayName: 'Me', hasProfilePicture: false, lastReadSequence: 0 },
    {
      id: 'other',
      userName: 'other',
      displayName: 'Other',
      hasProfilePicture: false,
      lastReadSequence: 0,
    },
  ],
  latestMessage: null,
  unreadCount: 0,
};
const message = (sequence: number): Message => ({
  id: `message-${sequence}`,
  conversationId: conversation.id,
  senderUserId: 'other',
  sequence,
  content: `Hello ${sequence}`,
  createdAtUtc: '2026-10-16T10:00:00Z',
  messageType: 'text',
  sharedPostId: null,
});

describe('Chat recovery and sending', () => {
  let stored: Message[];
  let page: ChatPage;
  let fail: boolean;
  let attempts: SendRequest[];
  let historyCursors: (number | undefined)[];
  beforeEach(async () => {
    stored = [];
    fail = false;
    attempts = [];
    historyCursors = [];
    const chat = {
      status: signal('Connected'),
      revision: signal(0),
      typing: signal(null),
      list: async () => ({ items: [conversation], totalPages: 1 }),
      get: async (id: string) => ({ ...conversation, id }),
      history: async (_id: string, before?: number) => {
        historyCursors.push(before);
        return stored
          .filter((m) => !before || m.sequence < before)
          .sort((a, b) => b.sequence - a.sequence)
          .slice(0, 50);
      },
      send: async (_id: string, request: SendRequest) => {
        attempts.push({ ...request });
        if (fail) throw new Error('timeout');
        return { ...message(1), id: request.id, content: request.content };
      },
      select: async () => {},
      read: async () => {},
      indicateTyping: () => {},
    };
    TestBed.configureTestingModule({
      imports: [ChatPage],
      providers: [
        provideRouter([]),
        { provide: ChatService, useValue: chat },
        { provide: AuthService, useValue: { user: signal({ id: 'me' }) } },
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap({})) } },
        { provide: SocialApiService, useValue: { profilePictureUrl: () => '' } },
      ],
    });
    const fixture = TestBed.createComponent(ChatPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    await page.choose(conversation);
  });
  it('deduplicates history reconciliation and loads every missed page after reconnect', async () => {
    stored = [message(1)];
    await page.refresh();
    stored = Array.from({ length: 125 }, (_, i) => message(i + 1));
    await page.refresh();
    await page.refresh();
    expect(page.messages().length).toBe(125);
    expect(new Set(page.messages().map((m) => m.id)).size).toBe(125);
    expect(page.messages()[124].sequence).toBe(125);
    expect(historyCursors).toContain(76);
    expect(historyCursors).toContain(26);
  });
  it('retains the same retry ID and payload after an unconfirmed send', async () => {
    page.draft = 'Original';
    fail = true;
    await page.send();
    page.draft = 'Edited during timeout';
    fail = false;
    await page.send();
    expect(attempts.length).toBe(2);
    expect(attempts[1]).toEqual(attempts[0]);
    expect(page.messages().length).toBe(1);
  });
  it('does not send a pending retry to a different conversation', async () => {
    page.draft = 'Original';
    fail = true;
    await page.send();
    await page.choose({ ...conversation, id: 'another' });
    fail = false;
    await page.send();
    expect(attempts.length).toBe(1);
    expect(page.error()).toContain('previous conversation');
  });
  it('loads older history without duplicating the latest page', async () => {
    stored = Array.from({ length: 65 }, (_, i) => message(i + 1));
    await page.refresh();
    expect(page.messages().length).toBe(50);
    await page.loadOlder();
    expect(page.messages().length).toBe(65);
    expect(page.older()).toBe(false);
  });
  it('does not send an empty message from the keyboard', async () => {
    page.draft = '   ';
    await page.send();
    expect(attempts).toHaveLength(0);
  });
  it('returns to the mobile inbox and clears the active history', async () => {
    stored = [message(1)];
    await page.refresh();
    page.hasNewMessages.set(true);
    page.closeConversation();
    expect(page.selected()).toBeNull();
    expect(page.messages()).toEqual([]);
    expect(page.hasNewMessages()).toBe(false);
  });
});
