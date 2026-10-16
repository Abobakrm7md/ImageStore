import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { InboxService } from '../../../core/notifications/inbox.service';
import { Icon } from '../icon/icon';

@Component({
  selector: 'app-notification-bell',
  imports: [RouterLink, RouterLinkActive, Icon],
  template: `<a
    routerLink="/notifications"
    routerLinkActive="active"
    class="bell"
    [attr.aria-label]="'Notifications, ' + inbox.unread() + ' unread'"
    [title]="inbox.error() || 'Notifications'"
  >
    <app-icon name="bell" [size]="20" /><span>Notifications</span>
    @if (inbox.unread()) {
      <b aria-live="polite">{{ inbox.unread() > 99 ? '99+' : inbox.unread() }}</b>
    }
    @if (inbox.error()) {
      <span aria-label="Refresh unavailable">!</span>
    }
  </a>`,
  styles: `
    .bell {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      position: relative;
    }
    b {
      border-radius: 12px;
      padding: 0.1rem 0.4rem;
      background: var(--color-primary, #665bd4);
      color: white;
      font-size: 0.75rem;
    }
  `,
})
export class NotificationBell {
  readonly inbox = inject(InboxService);
}
