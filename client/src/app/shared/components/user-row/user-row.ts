import { ChangeDetectionStrategy, Component, effect, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { UserSummary } from '../../../core/models/api.models';
import { Avatar } from '../avatar/avatar';
import { FollowButton } from '../follow-button/follow-button';

/** One user in a list (search results, followers, following) with an inline follow toggle. */
@Component({
  selector: 'app-user-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Avatar, FollowButton],
  template: `
    @let person = user();
    <div class="user-row card">
      <app-avatar [user]="person" [size]="42" />
      <div class="user-row-text">
        <a class="user-row-name" [routerLink]="['/users', person.id]">{{ person.displayName }}</a>
        <p class="user-row-username muted">&#64;{{ person.userName }}</p>
      </div>
      @if (person.id !== currentUserId()) {
        <app-follow-button
          [userId]="person.id"
          [following]="followingState"
          (followingChange)="onFollowingChange($event)"
        />
      }
    </div>
  `,
  styles: `
    .user-row {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.7rem 1rem;
    }

    .user-row-text {
      flex: 1;
      min-width: 0;
    }

    .user-row-name {
      font-weight: 600;
      color: var(--color-text);
      text-decoration: none;
    }

    .user-row-name:hover {
      color: var(--color-primary);
      text-decoration: underline;
    }

    .user-row-username {
      margin: 0;
      font-size: 0.85rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
})
export class UserRow {
  readonly user = input.required<UserSummary>();
  readonly currentUserId = input.required<string>();

  /** Emits after a successful follow/unfollow so lists can update their cached data. */
  readonly changed = output<boolean>();

  /** Local follow state, seeded from the row data and kept across toggles. */
  protected followingState = false;

  protected onFollowingChange(following: boolean): void {
    this.followingState = following;
    this.changed.emit(following);
  }

  private readonly lastUserId = signal<string | null>(null);

  constructor() {
    // Re-seed whenever a different user is rendered in this row (list recycling).
    effect(() => {
      const person = this.user();
      if (this.lastUserId() !== person.id) {
        this.lastUserId.set(person.id);
        this.followingState = person.isFollowing ?? false;
      }
    });
  }
}
