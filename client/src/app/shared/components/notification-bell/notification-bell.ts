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
    ariaCurrentWhenActive="page"
    [attr.aria-label]="'Notifications, ' + inbox.unread() + ' unread'"
    [title]="inbox.error() || 'Notifications'"
  >
    <app-icon name="bell" [size]="20" /><span class="visually-hidden">Notifications</span>
    @if (inbox.unread()) {
      <b aria-live="polite">{{ inbox.unread() > 99 ? '99+' : inbox.unread() }}</b>
    }
    @if (inbox.error()) {
      <span aria-label="Refresh unavailable">!</span>
    }
  </a>`,
  styles: `
    :host {
      display: inline-flex;
    }
    .bell {
      position: relative;
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border-radius: var(--radius-sm);
      color: var(--color-text-muted);
      text-decoration: none;
    }
    .bell:hover,
    .bell.active {
      color: var(--color-primary);
      background: var(--color-primary-soft);
    }
    b {
      position: absolute;
      top: 0;
      right: 0;
      min-width: 17px;
      padding: 1px 4px;
      border-radius: 6px;
      border: 2px solid #141519;
      font-size: 0.62rem;
      line-height: 1.4;
      font-weight: 650;
      background: var(--color-primary);
      color: #171321;
    }
    @media (max-width: 600px) {
      .bell {
        width: 38px;
      }
    }
  `,
})
export class NotificationBell {
  readonly inbox = inject(InboxService);
}
