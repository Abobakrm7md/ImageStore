import { DestroyRef, Injectable, effect, inject, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { ChatService } from '../chat/chat.service';
import { NotificationService } from './notification.service';
import { NotificationSound } from './notification-sound.service';
import {
  InboxNotification,
  NotificationPage,
  NotificationPreferences,
  NotificationType,
  defaultPreferences,
  presentationEnabled,
} from './inbox.models';

@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly chat = inject(ChatService);
  private readonly toast = inject(NotificationService);
  private readonly router = inject(Router);
  readonly sound = inject(NotificationSound);
  readonly unread = signal(0);
  readonly preferences = signal<NotificationPreferences>(defaultPreferences);
  readonly revision = signal(0);
  readonly error = signal('');
  private readonly seen = new Set<string>();
  private refreshVersion = 0;
  private readonly tabId = crypto.randomUUID();
  private presenceUser?: string;
  constructor() {
    this.chat.notificationEvents
      .pipe(takeUntilDestroyed())
      .subscribe((id) => void this.onEvent(id));
    effect((cleanup) => {
      const user = this.auth.user();
      this.unread.set(0);
      this.preferences.set(defaultPreferences);
      this.seen.clear();
      this.error.set('');
      ++this.refreshVersion;
      if (!user) return;
      void this.refresh();
      const timer = setInterval(() => void this.refresh(), 30000);
      this.presenceUser = user.id;
      const presence = () => this.updatePresence(user.id);
      const heartbeat = setInterval(presence, 5000);
      window.addEventListener('focus', presence);
      window.addEventListener('blur', presence);
      document.addEventListener('visibilitychange', presence);
      untracked(presence);
      const unlock = () => {
        if (this.preferences().soundEnabled) void this.sound.unlock();
      };
      document.addEventListener('pointerdown', unlock);
      document.addEventListener('keydown', unlock);
      cleanup(() => {
        this.presenceUser = undefined;
        untracked(() => this.updatePresence(user.id, true));
        clearInterval(heartbeat);
        window.removeEventListener('focus', presence);
        window.removeEventListener('blur', presence);
        document.removeEventListener('visibilitychange', presence);
        clearInterval(timer);
        document.removeEventListener('pointerdown', unlock);
        document.removeEventListener('keydown', unlock);
        this.sound.reset();
      });
    });
    effect(() => {
      this.chat.activeConversation();
      if (this.presenceUser) this.updatePresence(this.presenceUser);
    });
    inject(DestroyRef).onDestroy(() => this.sound.reset());
  }
  async refresh() {
    const user = this.auth.user()?.id;
    if (!user) return false;
    const version = ++this.refreshVersion;
    try {
      const [count, preferences] = await Promise.all([
        firstValueFrom(this.http.get<{ count: number }>('/api/notifications/unread-count')),
        firstValueFrom(this.http.get<NotificationPreferences>('/api/notifications/preferences')),
      ]);
      if (this.auth.user()?.id !== user || version !== this.refreshVersion) return false;
      this.unread.set(count.count);
      this.preferences.set(preferences);
      this.error.set('');
      this.revision.update((n) => n + 1);
      return true;
    } catch {
      if (this.auth.user()?.id === user) this.error.set('Notifications could not be refreshed.');
      return false;
    }
  }
  list(page = 1, unread?: boolean, type?: NotificationType) {
    return firstValueFrom(
      this.http.get<NotificationPage>('/api/notifications', {
        params: {
          page,
          pageSize: 20,
          ...(unread === undefined ? {} : { unread }),
          ...(type ? { type } : {}),
        },
      }),
    );
  }
  async read(id: string) {
    await firstValueFrom(this.http.patch(`/api/notifications/${id}/read`, {}));
    await this.refresh();
  }
  async readAll() {
    await firstValueFrom(this.http.patch('/api/notifications/read-all', {}));
    await this.refresh();
  }
  async remove(id: string) {
    await firstValueFrom(this.http.delete(`/api/notifications/${id}`));
    await this.refresh();
  }
  async clearRead() {
    await firstValueFrom(this.http.delete('/api/notifications/read'));
    await this.refresh();
  }
  async savePreferences(preferences: NotificationPreferences) {
    const saved = await firstValueFrom(
      this.http.put<NotificationPreferences>('/api/notifications/preferences', preferences),
    );
    this.preferences.set(saved);
    await this.refresh();
  }
  async open(n: InboxNotification) {
    if (!n.readAtUtc) await this.read(n.id);
    // Re-fetch after marking read: visibility may have changed while the panel was open.
    const current = await firstValueFrom(
      this.http.get<InboxNotification>(`/api/notifications/${n.id}`),
    );
    if (!current.available) {
      this.toast.info('This activity is no longer available.');
      return;
    }
    if (current.conversationId)
      await this.router.navigate(['/chat'], {
        queryParams: { conversation: current.conversationId },
      });
    else if (current.postId)
      await this.router.navigate(['/posts', current.postId], {
        fragment: 'comments',
        queryParams: current.commentId ? { comment: current.commentId } : {},
      });
    else if (current.actorUserId) await this.router.navigate(['/users', current.actorUserId]);
  }
  private async onEvent(id: string | null) {
    const user = this.auth.user()?.id;
    if (!user) return;
    if (!(await this.refresh())) return;
    if (!id || this.auth.user()?.id !== user) return;
    try {
      const n = await firstValueFrom(this.http.get<InboxNotification>(`/api/notifications/${id}`));
      if (
        this.auth.user()?.id !== user ||
        n.readAtUtc ||
        !n.available ||
        n.actorUserId === user ||
        !presentationEnabled(n, this.preferences()) ||
        Date.now() - Date.parse(n.createdAtUtc) > 60000
      )
        return;
      const show = () => {
        if (this.auth.user()?.id !== user || !this.claim(user, id)) return;
        if (
          n.conversationId &&
          this.preferences().suppressActiveConversation &&
          this.activeSomewhere(user, n.conversationId)
        )
          return;
        this.toast.info(`${n.actorDisplayName ? n.actorDisplayName + ': ' : ''}${n.text}`);
        if (this.preferences().soundEnabled) this.sound.play(this.preferences().soundVolume);
      };
      // Serialize claims across tabs where supported. The fallback still deduplicates within a tab.
      if (navigator.locks) await navigator.locks.request(`imagestore-notice-${user}`, show);
      else show();
    } catch {
      /* Deleted or unavailable item; the inbox/count refresh still succeeds. */
    }
  }
  private updatePresence(user: string, clear = false) {
    try {
      const key = `imagestore-active-chat-${user}`;
      const previous = JSON.parse(localStorage.getItem(key) ?? 'null');
      const conversationId = this.chat.activeConversation();
      if (
        !clear &&
        conversationId &&
        document.hasFocus() &&
        document.visibilityState === 'visible'
      ) {
        localStorage.setItem(
          key,
          JSON.stringify({ tab: this.tabId, conversationId, until: Date.now() + 15000 }),
        );
      } else if (previous?.tab === this.tabId) localStorage.removeItem(key);
    } catch {
      /* Local active-conversation suppression still works without storage. */
    }
  }
  private activeSomewhere(user: string, conversationId: string): boolean {
    if (
      conversationId === this.chat.activeConversation() &&
      document.hasFocus() &&
      document.visibilityState === 'visible'
    )
      return true;
    try {
      const active = JSON.parse(localStorage.getItem(`imagestore-active-chat-${user}`) ?? 'null');
      return active?.conversationId === conversationId && active.until > Date.now();
    } catch {
      return false;
    }
  }
  private claim(user: string, id: string): boolean {
    if (this.seen.has(id)) return false;
    this.seen.add(id);
    if (this.seen.size > 500) this.seen.delete(this.seen.values().next().value!);
    try {
      const key = `imagestore-notice-seen-${user}`;
      const stored: unknown = JSON.parse(localStorage.getItem(key) ?? '[]');
      const ids = Array.isArray(stored)
        ? stored.filter((v): v is string => typeof v === 'string')
        : [];
      if (ids.includes(id)) return false;
      localStorage.setItem(key, JSON.stringify([...ids.slice(-499), id]));
    } catch {
      /* Restricted storage must not prevent notifications. */
    }
    return true;
  }
}
