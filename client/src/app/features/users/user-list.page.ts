import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { SocialApiService } from '../../core/social/social-api.service';
import { UserSummary } from '../../core/models/api.models';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { Pagination } from '../../shared/components/pagination/pagination';
import { UserRow } from '../../shared/components/user-row/user-row';

export const USER_LIST_PAGE_SIZE = 20;

@Component({
  selector: 'app-user-list-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, Pagination, UserRow],
  templateUrl: './user-list.page.html',
  styleUrl: './user-list.page.css',
})
export class UserListPage {
  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  private readonly social = inject(SocialApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  protected readonly currentUserId = computed(() => this.auth.user()?.id ?? '');

  /** 'followers' or 'following' – derived from the last URL segment. */
  protected readonly kind = computed<'followers' | 'following'>(() => {
    const segments = this.routeUrl() ?? [];
    return segments[segments.length - 1]?.path === 'following' ? 'following' : 'followers';
  });

  private readonly routeUrl = toSignal(this.route.url);

  protected readonly heading = computed(() =>
    this.kind() === 'following' ? 'Following' : 'Followers',
  );

  protected readonly page = signal(1);

  protected readonly list = rxResource({
    params: () => ({ userId: this.id(), kind: this.kind(), page: this.page() }),
    stream: ({ params }) =>
      params.kind === 'following'
        ? this.social.following(params.userId, params.page, USER_LIST_PAGE_SIZE)
        : this.social.followers(params.userId, params.page, USER_LIST_PAGE_SIZE),
  });

  protected onFollowChanged(user: UserSummary, following: boolean): void {
    // Keep the row's isFollowing flag in sync with the server response.
    const result = this.list.value();
    if (!result) return;
    this.list.set({
      ...result,
      items: result.items.map((item) =>
        item.id === user.id ? { ...item, isFollowing: following } : item,
      ),
    });
  }

  protected goToPage(next: number): void {
    this.page.set(next);
  }

  protected isNotFound(error: unknown): boolean {
    return error instanceof HttpErrorResponse && error.status === 404;
  }
}
