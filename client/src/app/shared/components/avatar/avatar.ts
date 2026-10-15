import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { SocialApiService } from '../../../core/social/social-api.service';
import { UserSummary } from '../../../core/models/api.models';
import { initials } from '../../utils/reactions';

/**
 * A user's avatar: the authorized profile-picture endpoint when they have one, otherwise their
 * initials. When the picture fails to load (removed, or access lost) the fallback appears.
 */
@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let person = user();
    <span class="avatar" [style.--avatar-size.px]="size()">
      @if (person.hasProfilePicture && !broken()) {
        <img
          [src]="api.profilePictureUrl(person.id)"
          [alt]="person.displayName + ' profile picture'"
          loading="lazy"
          decoding="async"
          (error)="broken.set(true)"
        />
      } @else {
        <span class="avatar-fallback" [attr.aria-label]="person.displayName" role="img">
          {{ fallback() }}
        </span>
      }
    </span>
  `,
  styles: `
    .avatar {
      display: inline-flex;
      width: var(--avatar-size, 40px);
      height: var(--avatar-size, 40px);
      border-radius: 50%;
      overflow: hidden;
      background: var(--color-surface-muted);
      border: 1px solid var(--color-border);
      flex-shrink: 0;
    }

    .avatar img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }

    .avatar-fallback {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 100%;
      height: 100%;
      font-weight: 600;
      font-size: calc(var(--avatar-size, 40px) * 0.38);
      color: var(--color-text-muted);
      user-select: none;
    }
  `,
})
export class Avatar {
  readonly user = input.required<UserSummary>();
  readonly size = input(40);

  protected readonly api = inject(SocialApiService);
  protected readonly broken = signal(false);
  protected readonly fallback = computed(() => initials(this.user().displayName));
}
