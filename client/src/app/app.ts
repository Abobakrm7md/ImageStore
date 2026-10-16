import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { ChatService } from './core/chat/chat.service';
import { AuthService } from './core/auth/auth.service';
import { NotificationService } from './core/notifications/notification.service';
import { ConfirmDialog } from './shared/components/confirm-dialog/confirm-dialog';
import { ToastContainer } from './shared/components/toast-container/toast-container';
import { Icon } from './shared/components/icon/icon';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, ToastContainer, ConfirmDialog, Icon],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly chat = inject(ChatService);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly menuOpen = signal(false);

  constructor() {
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.menuOpen.set(false));
  }

  protected logout(): void {
    this.auth
      .logout()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.notifications.info('You have been signed out.');
        void this.router.navigateByUrl('/login');
      });
  }
}
