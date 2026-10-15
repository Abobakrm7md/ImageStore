import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, of } from 'rxjs';
import { ImagePost, PagedResult } from '../../core/models/api.models';
import { SocialApiService } from '../../core/social/social-api.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { Icon } from '../../shared/components/icon/icon';
import { Avatar } from '../../shared/components/avatar/avatar';
import { FollowButton } from '../../shared/components/follow-button/follow-button';
import { Pagination } from '../../shared/components/pagination/pagination';
import { PostCard } from '../../shared/components/post-card/post-card';

export const PROFILE_POSTS_PAGE_SIZE = 6;

const EMPTY_POSTS: Observable<PagedResult<ImagePost>> = of({
  items: [],
  page: 1,
  pageSize: PROFILE_POSTS_PAGE_SIZE,
  totalCount: 0,
  totalPages: 0,
});

@Component({
  selector: 'app-user-profile-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, Avatar, EmptyState, FollowButton, Icon, Pagination, PostCard],
  templateUrl: './user-profile.page.html',
  styleUrl: './user-profile.page.css',
})
export class UserProfilePage {
  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  protected readonly social = inject(SocialApiService);

  protected readonly profile = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.social.profile(params),
  });

  protected readonly notFound = computed(() => {
    const error = this.profile.error();
    return error instanceof HttpErrorResponse && error.status === 404;
  });

  /** Local follow state so toggling updates the header without waiting for the reload. */
  protected readonly followingOverride = signal<boolean | null>(null);
  protected readonly isFollowing = computed(
    () => this.followingOverride() ?? this.profile.value()?.isFollowing ?? false,
  );

  protected readonly postsVisible = computed(() => {
    const profile = this.profile.value();
    return profile !== undefined && (profile.isCurrentUser || this.isFollowing());
  });

  protected readonly postsPage = signal(1);

  /**
   * The published gallery is only fetched when it is actually visible – the API answers 404
   * for non-followers, and the SPA must not rely on navigation alone for authorization.
   */
  protected readonly userPosts = rxResource({
    params: () => ({ userId: this.id(), page: this.postsPage(), visible: this.postsVisible() }),
    stream: ({ params }) =>
      params.visible
        ? this.social.userPosts(params.userId, params.page, PROFILE_POSTS_PAGE_SIZE)
        : EMPTY_POSTS,
  });

  constructor() {
    // Reset transient state when the component is reused for another user (profile → profile).
    effect(() => {
      this.id();
      this.followingOverride.set(null);
      this.postsPage.set(1);
    });
  }

  protected onFollowChanged(following: boolean): void {
    this.followingOverride.set(following);
    this.postsPage.set(1);
    // Refresh counts (followers/postCount) from the server.
    this.profile.reload();
  }

  protected goToPostsPage(page: number): void {
    this.postsPage.set(page);
  }
}
