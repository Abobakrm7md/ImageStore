import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ChatService } from '../chat/chat.service';
import { InboxService } from './inbox.service';
import { NotificationService } from './notification.service';
import { NotificationSound } from './notification-sound.service';
import {
  InboxNotification,
  defaultPreferences,
  presentationEnabled,
  relativeTime,
} from './inbox.models';

const notice = (): InboxNotification => ({
  id: crypto.randomUUID(),
  type: 'chatMessage',
  text: 'Sent you a private message.',
  actorDisplayName: 'Someone',
  actorUserId: 'other',
  createdAtUtc: new Date().toISOString(),
  readAtUtc: null,
  critical: false,
  available: true,
  postId: null,
  commentId: null,
  conversationId: 'chat',
  messageId: 'message',
});
const settle = async () => {
  for (let i = 0; i < 12; i++) await Promise.resolve();
};

describe('Notification presentation policy', () => {
  it('allows critical notices even when all in-app notifications are disabled', () => {
    expect(
      presentationEnabled(
        { ...notice(), critical: true, type: 'systemAnnouncement' },
        { ...defaultPreferences, inAppEnabled: false },
      ),
    ).toBe(true);
    expect(presentationEnabled(notice(), { ...defaultPreferences, inAppEnabled: false })).toBe(
      false,
    );
  });
  it('separates sound preference from durable inbox presentation', () => {
    expect(presentationEnabled(notice(), { ...defaultPreferences, soundEnabled: false })).toBe(
      true,
    );
    expect(presentationEnabled(notice(), { ...defaultPreferences, chatEnabled: false })).toBe(
      false,
    );
  });
  it('formats relative timestamps without going negative', () => {
    expect(relativeTime('2026-10-16T10:00:00Z', Date.parse('2026-10-16T10:02:00Z'))).toBe(
      '2 min ago',
    );
    expect(relativeTime('2026-10-17T10:00:00Z', Date.parse('2026-10-16T10:02:00Z'))).toBe(
      'Just now',
    );
  });
});

describe('Persistent inbox realtime integration', () => {
  let inbox: InboxService;
  let http: HttpTestingController;
  let events: Subject<string | null>;
  let active: ReturnType<typeof signal<string | undefined>>;
  let sound: {
    play: ReturnType<typeof vi.fn>;
    unlock: ReturnType<typeof vi.fn>;
    reset: ReturnType<typeof vi.fn>;
  };
  let toast: { info: ReturnType<typeof vi.fn> };
  let prefs = { ...defaultPreferences, soundEnabled: true };
  function refresh(count = 1) {
    http.expectOne('/api/notifications/unread-count').flush({ count });
    http.expectOne('/api/notifications/preferences').flush(prefs);
  }
  async function event(n: InboxNotification) {
    events.next(n.id);
    refresh();
    await settle();
    http.expectOne(`/api/notifications/${n.id}`).flush(n);
    await settle();
  }
  beforeEach(async () => {
    localStorage.clear();
    prefs = { ...defaultPreferences, soundEnabled: true };
    events = new Subject();
    active = signal<string | undefined>(undefined);
    sound = { play: vi.fn(), unlock: vi.fn(), reset: vi.fn() };
    toast = { info: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user: signal({ id: 'me' }) } },
        {
          provide: ChatService,
          useValue: { notificationEvents: events, activeConversation: active },
        },
        { provide: NotificationSound, useValue: sound },
        { provide: NotificationService, useValue: toast },
      ],
    });
    inbox = TestBed.inject(InboxService);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
    refresh(0);
    await settle();
  });
  afterEach(() => {
    try {
      http.verify();
    } finally {
      vi.restoreAllMocks();
      TestBed.resetTestingModule();
    }
  });
  it('refreshes the count and alerts only once when an event is replayed', async () => {
    const n = notice();
    await event(n);
    await event(n);
    expect(inbox.unread()).toBe(1);
    expect(toast.info).toHaveBeenCalledTimes(1);
    expect(sound.play).toHaveBeenCalledTimes(1);
    expect(sound.play).toHaveBeenCalledWith(35);
  });
  it('never replays sounds when reconnecting or fetching history', async () => {
    events.next(null);
    refresh(7);
    await settle();
    expect(inbox.unread()).toBe(7);
    expect(sound.play).not.toHaveBeenCalled();
  });
  it('does not play sound when disabled but still presents the notification', async () => {
    prefs.soundEnabled = false;
    await event(notice());
    expect(toast.info).toHaveBeenCalledTimes(1);
    expect(sound.play).not.toHaveBeenCalled();
  });
  it('suppresses alerts in the focused active conversation', async () => {
    vi.spyOn(document, 'hasFocus').mockReturnValue(true);
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible');
    active.set('chat');
    TestBed.tick();
    await event(notice());
    expect(sound.play).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
  });
  it('suppresses stale, own, and already-read activity', async () => {
    await event({ ...notice(), createdAtUtc: '2020-01-01T00:00:00Z' });
    await event({ ...notice(), actorUserId: 'me' });
    await event({ ...notice(), readAtUtc: new Date().toISOString() });
    expect(sound.play).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
  });
  it('honors another tab’s fresh active-conversation presence', async () => {
    localStorage.setItem(
      'imagestore-active-chat-me',
      JSON.stringify({ tab: 'another', conversationId: 'chat', until: Date.now() + 10000 }),
    );
    await event(notice());
    expect(sound.play).not.toHaveBeenCalled();
  });
  it('does not repeat an alert already claimed by another tab', async () => {
    const n = notice();
    localStorage.setItem('imagestore-notice-seen-me', JSON.stringify([n.id]));
    await event(n);
    expect(sound.play).not.toHaveBeenCalled();
    expect(toast.info).not.toHaveBeenCalled();
  });
  it('delegates pagination and filters to the server', async () => {
    const response = inbox.list(3, true, 'commentMention');
    const request = http.expectOne((r) => r.url === '/api/notifications');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('unread')).toBe('true');
    expect(request.request.params.get('type')).toBe('commentMention');
    request.flush({ items: [], page: 3, totalPages: 0, totalCount: 0 });
    await response;
  });
});

describe('Notification sound failure handling', () => {
  it('does not construct audio during automatic playback before a gesture', () => {
    const sound = new NotificationSound();
    const audio = vi.fn();
    vi.stubGlobal('AudioContext', audio);
    sound.play(40);
    expect(audio).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
  it('handles unavailable audio without throwing', async () => {
    vi.stubGlobal(
      'AudioContext',
      class {
        constructor() {
          throw new Error('unsupported');
        }
      },
    );
    const sound = new NotificationSound();
    await sound.test(40);
    expect(sound.status()).toContain('unavailable');
    vi.unstubAllGlobals();
  });
});
