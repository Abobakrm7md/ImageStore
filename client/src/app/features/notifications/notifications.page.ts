import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InboxService } from '../../core/notifications/inbox.service';
import {
  InboxNotification,
  NotificationPreferences,
  NotificationType,
  relativeTime,
} from '../../core/notifications/inbox.models';
import { Icon, IconName } from '../../shared/components/icon/icon';
import { Pagination } from '../../shared/components/pagination/pagination';

@Component({
  selector: 'app-notifications-page',
  imports: [FormsModule, DatePipe, Icon, Pagination],
  templateUrl: './notifications.page.html',
  styleUrl: './notifications.page.css',
})
export class NotificationsPage {
  readonly inbox = inject(InboxService);
  readonly items = signal<InboxNotification[]>([]);
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly feedback = signal('');
  readonly now = signal(Date.now());
  readonly relativeTime = relativeTime;
  page = 1;
  totalPages = 0;
  filter = 'all';
  type: NotificationType | '' = '';
  settings: NotificationPreferences = { ...this.inbox.preferences() };
  editingSettings = false;
  private version = 0;
  readonly types: { value: NotificationType; label: string }[] = [
    { value: 'chatMessage', label: 'Messages' },
    { value: 'sharedPost', label: 'Shared posts' },
    { value: 'newFollower', label: 'Followers' },
    { value: 'postComment', label: 'Comments' },
    { value: 'commentReply', label: 'Replies' },
    { value: 'commentMention', label: 'Mentions' },
    { value: 'postReaction', label: 'Reactions' },
    { value: 'systemAnnouncement', label: 'Announcements' },
  ];
  constructor() {
    effect(() => {
      this.inbox.revision();
      void this.load();
    });
    effect(() => {
      const p = this.inbox.preferences();
      if (!this.editingSettings) this.settings = { ...p };
    });
    const timer = setInterval(() => this.now.set(Date.now()), 30000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      ++this.version;
    });
  }
  async load(page = this.page) {
    const version = ++this.version;
    this.page = page;
    this.loading.set(true);
    try {
      const result = await this.inbox.list(
        page,
        this.filter === 'unread' ? true : undefined,
        this.type || undefined,
      );
      if (version !== this.version) return;
      this.items.set(result.items);
      this.totalPages = result.totalPages;
      this.error.set('');
      if (page > Math.max(1, result.totalPages)) {
        void this.load(Math.max(1, result.totalPages));
      }
    } catch {
      if (version === this.version) this.error.set('Could not load notifications. Try again.');
    } finally {
      if (version === this.version) this.loading.set(false);
    }
  }
  icon(n: InboxNotification): IconName {
    switch (n.type) {
      case 'commentMention':
        return 'mention';
      case 'commentReply':
        return 'reply';
      case 'newFollower':
        return 'user';
      case 'postReaction':
        return 'heart';
      case 'sharedPost':
        return 'image';
      case 'systemAnnouncement':
        return 'alert';
      default:
        return 'message';
    }
  }
  async action(
    action: 'open' | 'read' | 'remove' | 'readAll' | 'clearRead',
    n?: InboxNotification,
  ) {
    this.busy.set(true);
    try {
      if (action === 'open' && n) await this.inbox.open(n);
      if (action === 'read' && n) await this.inbox.read(n.id);
      if (action === 'remove' && n) await this.inbox.remove(n.id);
      if (action === 'readAll') await this.inbox.readAll();
      if (action === 'clearRead') await this.inbox.clearRead();
      this.feedback.set(
        action === 'open'
          ? ''
          : action === 'clearRead'
            ? 'Read notifications cleared.'
            : action === 'remove'
              ? 'Notification deleted.'
              : 'Marked as read.',
      );
      await this.load();
    } catch {
      this.error.set('The action could not be completed. Refresh and try again.');
    } finally {
      this.busy.set(false);
    }
  }
  async save() {
    this.busy.set(true);
    try {
      await this.inbox.savePreferences(this.settings);
      this.settings = { ...this.inbox.preferences() };
      this.editingSettings = false;
      this.feedback.set('Preferences saved.');
      this.error.set('');
    } catch {
      this.error.set('Could not save preferences. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
