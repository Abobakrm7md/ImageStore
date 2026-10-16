import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../../core/http/api-error';
import { NotificationService } from '../../../core/notifications/notification.service';
import { SocialApiService } from '../../../core/social/social-api.service';

/**
 * Follow/unfollow toggle for one user. Calls the API itself and reports the new state through
 * the model input and the `changed` output, so parents can refresh counts without a reload.
 * Never rendered for the current user.
 */
@Component({
  selector: 'app-follow-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="btn btn-sm"
      [class.btn-primary]="!following()"
      [class.btn-secondary]="following()"
      [disabled]="pending()"
      [attr.aria-pressed]="following()"
      (click)="toggle()"
    >
      @if (pending()) {
        <span class="spinner" aria-hidden="true"></span>
      }
      {{ following() ? 'Following' : 'Follow' }}
    </button>
  `,
})
export class FollowButton {
  /** The user this button follows. */
  readonly userId = input.required<string>();

  /** Current follow state; updated locally after a successful toggle. */
  readonly following = model.required<boolean>();

  /** Emits the new state after a successful follow/unfollow. */
  readonly changed = output<boolean>();

  private readonly api = inject(SocialApiService);
  private readonly notifications = inject(NotificationService);

  protected readonly pending = signal(false);

  protected async toggle(): Promise<void> {
    if (this.pending()) {
      return;
    }

    const target = !this.following();
    this.pending.set(true);
    try {
      await firstValueFrom(target ? this.api.follow(this.userId()) : this.api.unfollow(this.userId()));
      this.following.set(target);
      this.changed.emit(target);
    } catch (error) {
      this.notifications.error(errorMessage(error));
    } finally {
      this.pending.set(false);
    }
  }
}
