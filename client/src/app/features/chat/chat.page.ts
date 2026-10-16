import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChatService, Conversation, Message, SendRequest } from '../../core/chat/chat.service';
import { AuthService } from '../../core/auth/auth.service';
import { Avatar } from '../../shared/components/avatar/avatar';

@Component({
  selector: 'app-chat-page',
  imports: [DatePipe, FormsModule, RouterLink, Avatar],
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
  page = 1;
  totalPages = 1;
  draft = '';
  share?: string;
  private pending?: { conversationId: string; request: SendRequest };
  private refreshing = false;
  private again = false;
  private destroyed = false;
  constructor() {
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
    this.older.set(true);
    this.error.set('');
    await this.chat.select(c.id);
    await this.refresh();
  }
  private merge(items: Message[]) {
    const map = new Map(this.messages().map((m) => [m.id, m]));
    items.forEach((m) => map.set(m.id, m));
    this.messages.set([...map.values()].sort((a, b) => a.sequence - b.sequence));
  }
  async loadOlder() {
    const id = this.selected()?.id;
    if (!id) return;
    try {
      const batch = await this.chat.history(id, this.messages()[0]?.sequence);
      if (this.selected()?.id !== id) return;
      this.merge(batch);
      this.older.set(batch.length === 50);
    } catch {
      this.error.set('Could not load older messages. Try again.');
    }
  }
  async markRead() {
    const c = this.selected();
    const latest = this.messages().at(-1);
    if (!c || !latest || document.visibilityState !== 'visible' || !document.hasFocus()) return;
    const me = c.participants.find((p) => p.id === this.auth.user()?.id)!;
    if (me.lastReadSequence >= latest.sequence) return;
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
  }
  async send() {
    const c = this.selected();
    if (!c || this.sending()) return;
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
      if (this.selected()?.id === c.id) this.merge([message]);
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
