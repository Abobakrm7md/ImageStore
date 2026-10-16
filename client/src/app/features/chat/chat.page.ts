import {
  Component,
  DestroyRef,
  ElementRef,
  afterEveryRender,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatService, Conversation, Message, SendRequest } from '../../core/chat/chat.service';
import { AuthService } from '../../core/auth/auth.service';
import { Icon } from '../../shared/components/icon/icon';
import { Avatar } from '../../shared/components/avatar/avatar';

@Component({
  selector: 'app-chat-page',
  imports: [DatePipe, FormsModule, RouterLink, Avatar, Icon],
  templateUrl: './chat.page.html',
  styleUrl: './chat.page.css',
})
export class ChatPage {
  readonly chat = inject(ChatService);
  readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  readonly conversations = signal<Conversation[]>([]);
  readonly selected = signal<Conversation | null>(null);
  readonly messages = signal<Message[]>([]);
  readonly error = signal('');
  readonly loading = signal(false);
  readonly sending = signal(false);
  readonly older = signal(false);
  readonly now = signal(Date.now());
  readonly loadingOlder = signal(false);
  readonly hasNewMessages = signal(false);
  private readonly history = viewChild<ElementRef<HTMLElement>>('history');
  private readonly chatWindow = viewChild<ElementRef<HTMLElement>>('chatWindow');
  private scrollPlan: 'bottom' | { height: number; top: number } | null = null;
  private focusConversation = false;
  private reading = false;
  page = 1;
  totalPages = 1;
  draft = '';
  share?: string;
  private pending?: { conversationId: string; request: SendRequest };
  private refreshing = false;
  private again = false;
  private destroyed = false;
  constructor() {
    afterEveryRender(() => {
      const element = this.history()?.nativeElement;
      if (element && this.scrollPlan) {
        element.scrollTop =
          this.scrollPlan === 'bottom'
            ? element.scrollHeight
            : element.scrollHeight - this.scrollPlan.height + this.scrollPlan.top;
        this.scrollPlan = null;
      }
      if (this.focusConversation && this.chatWindow()) {
        this.chatWindow()!.nativeElement.focus({ preventScroll: true });
        this.focusConversation = false;
      }
    });
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.destroyed = true;
      void this.chat.select();
    });
    this.route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
      this.share = params.get('share') ?? undefined;
      const conversation = params.get('conversation');
      if (conversation)
        void this.chat
          .get(conversation)
          .then((c) => this.choose(c))
          .catch(() => this.error.set('Conversation is unavailable.'));
      const user = params.get('user');
      if (user)
        void this.chat
          .open(user)
          .then((c) => this.choose(c))
          .catch(() => this.error.set('Could not open conversation.'));
    });
    effect(() => {
      this.chat.revision();
      void this.refresh();
    });
    // Periodic reconciliation also covers the persist/broadcast crash window.
    const reconcile = setInterval(() => void this.refresh(), 30000);
    inject(DestroyRef).onDestroy(() => clearInterval(reconcile));
  }
  other(c: Conversation) {
    return c.participants.find((p) => p.id !== this.auth.user()?.id)!;
  }
  async refresh() {
    if (this.refreshing) {
      this.again = true;
      return;
    }
    this.refreshing = true;
    this.loading.set(true);
    try {
      do {
        this.again = false;
        const page = await this.chat.list(this.page);
        if (this.destroyed) return;
        this.conversations.set(page.items);
        this.totalPages = page.totalPages;
        const id = this.selected()?.id;
        if (id) {
          const summary = await this.chat.get(id);
          const high = Math.max(0, ...this.messages().map((m) => m.sequence));
          let batch = await this.chat.history(id);
          const recovered = [...batch];
          // Recover all missed messages, not only the latest page, after a long outage.
          while (
            high > 0 &&
            batch.length === 50 &&
            Math.min(...batch.map((m) => m.sequence)) > high
          ) {
            batch = await this.chat.history(id, Math.min(...batch.map((m) => m.sequence)));
            recovered.push(...batch);
          }
          if (this.selected()?.id === id && !this.destroyed) {
            this.selected.set(summary);
            this.merge(recovered);
            if (high === 0) this.older.set(recovered.length === 50);
            await this.markRead();
          }
        }
      } while (this.again && !this.destroyed);
    } catch {
      this.error.set('Could not refresh messages. Try again.');
    } finally {
      this.refreshing = false;
      this.loading.set(false);
    }
  }
  async choose(c: Conversation) {
    this.selected.set(c);
    this.messages.set([]);
    this.hasNewMessages.set(false);
    this.focusConversation = true;
    this.older.set(true);
    this.error.set('');
    await this.chat.select(c.id);
    await this.refresh();
  }
  private merge(items: Message[], followLatest = true) {
    const viewport = this.history()?.nativeElement;
    const atBottom =
      !viewport || viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight < 80;
    const initial = this.messages().length === 0;
    const oldLast = this.messages().at(-1)?.sequence ?? 0;
    if (followLatest && (initial || atBottom)) this.scrollPlan = 'bottom';
    else if (followLatest && items.some((m) => m.sequence > oldLast)) this.hasNewMessages.set(true);
    const map = new Map(this.messages().map((m) => [m.id, m]));
    items.forEach((m) => map.set(m.id, m));
    this.messages.set([...map.values()].sort((a, b) => a.sequence - b.sequence));
  }
  async loadOlder() {
    const id = this.selected()?.id;
    if (!id || this.loadingOlder()) return;
    this.loadingOlder.set(true);
    try {
      const batch = await this.chat.history(id, this.messages()[0]?.sequence);
      if (this.selected()?.id !== id) return;
      const viewport = this.history()?.nativeElement;
      const offset = viewport ? { height: viewport.scrollHeight, top: viewport.scrollTop } : null;
      this.merge(batch, false);
      this.scrollPlan = offset;
      this.older.set(batch.length === 50);
    } catch {
      this.error.set('Could not load older messages. Try again.');
    } finally {
      this.loadingOlder.set(false);
    }
  }
  async markRead() {
    if (this.reading) return;
    const viewport = this.history()?.nativeElement;
    if (
      viewport &&
      this.scrollPlan !== 'bottom' &&
      viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight > 80
    )
      return;
    const c = this.selected();
    const latest = this.messages().at(-1);
    if (!c || !latest || document.visibilityState !== 'visible' || !document.hasFocus()) return;
    const me = c.participants.find((p) => p.id === this.auth.user()?.id)!;
    if (me.lastReadSequence >= latest.sequence) return;
    this.reading = true;
    try {
      await this.chat.read(c.id, latest.sequence);
      if (this.selected()?.id === c.id)
        this.selected.update((value) =>
          value
            ? {
                ...value,
                participants: value.participants.map((p) =>
                  p.id === me.id ? { ...p, lastReadSequence: latest.sequence } : p,
                ),
              }
            : value,
        );
    } catch {
      /* Retry read synchronization on the next refresh/focus without an unhandled rejection. */
    } finally {
      this.reading = false;
    }
  }
  closeConversation() {
    this.selected.set(null);
    this.messages.set([]);
    this.hasNewMessages.set(false);
    void this.chat.select();
  }
  showLatest() {
    this.scrollPlan = 'bottom';
    this.hasNewMessages.set(false);
    void this.markRead();
  }
  onHistoryScroll() {
    const el = this.history()?.nativeElement;
    if (el && el.scrollHeight - el.scrollTop - el.clientHeight < 80) {
      this.hasNewMessages.set(false);
      void this.markRead();
    }
  }

  async send() {
    const c = this.selected();
    if (!c || this.sending() || (!this.pending && !this.draft.trim() && !this.share)) return;
    if (this.pending && this.pending.conversationId !== c.id) {
      this.error.set(
        'Return to the previous conversation to retry the pending message, or discard it.',
      );
      return;
    }
    if (!this.pending)
      this.pending = {
        conversationId: c.id,
        request: { id: crypto.randomUUID(), content: this.draft, sharedPostId: this.share },
      };
    this.sending.set(true);
    this.error.set('');
    try {
      const message = await this.chat.send(c.id, this.pending.request);
      if (this.selected()?.id === c.id) {
        this.merge([message]);
        this.scrollPlan = 'bottom';
        this.hasNewMessages.set(false);
      }
      this.pending = undefined;
      this.draft = '';
      this.share = undefined;
      await this.refresh();
    } catch {
      this.error.set(
        'Message not confirmed. Retry sends the same message safely. A shared post must be visible to both people.',
      );
    } finally {
      this.sending.set(false);
    }
  }
  discard() {
    this.pending = undefined;
    this.error.set('');
  }
  key(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void this.send();
    }
  }
  async move(delta: number) {
    this.page += delta;
    await this.refresh();
  }
}
