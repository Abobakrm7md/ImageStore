import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ImagePost, ReactionSummary } from '../../../core/models/api.models';
import { PostApiService } from '../../../core/posts/post-api.service';
import { Avatar } from '../avatar/avatar';
import { Icon } from '../icon/icon';
import { ReactionBar } from '../reaction-bar/reaction-bar';

/**
 * A feed/profile card for one image post: the image (served by the authorized post-content
 * endpoint), author link, reaction bar and comment count. Reaction updates are applied locally,
 * so counts refresh without reloading the list.
 */
@Component({
  selector: 'app-post-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, Avatar, Icon, ReactionBar],
  template: `
    @let post = displayPost();
    <article class="post-card card">
      <header class="post-head">
        <app-avatar [user]="post.author" [size]="38" />
        <div class="post-head-text">
          <a class="post-author" [routerLink]="['/users', post.author.id]">
            {{ post.author.displayName }}
          </a>
          <p class="post-meta">
            <span class="post-username">&#64;{{ post.author.userName }}</span>
            <span aria-hidden="true">·</span>
            <time [attr.datetime]="post.createdAtUtc">{{
              post.createdAtUtc | date: 'mediumDate'
            }}</time>
            @if (post.updatedAtUtc) {
              <span class="muted">(edited)</span>
            }
          </p>
        </div>
      </header>

      <a
        class="post-image"
        [routerLink]="['/posts', post.id]"
        [attr.aria-label]="'Open post: ' + post.title"
      >
        @if (!broken()) {
          <img
            [src]="api.contentUrl(post.id)"
            [alt]="post.title"
            loading="lazy"
            decoding="async"
            (error)="broken.set(true)"
          />
        } @else {
          <span class="post-image-fallback"><app-icon name="image" [size]="40" /></span>
        }
      </a>

      <div class="card-body">
        <h2 class="card-title">
          <a [routerLink]="['/posts', post.id]">{{ post.title }}</a>
        </h2>
        @if (post.description) {
          <p class="post-description">{{ post.description }}</p>
        }
        <div class="post-actions">
          <a routerLink="/chat" [queryParams]="{ share: post.id }">Share in message</a>
          <app-reaction-bar
            [postId]="post.id"
            [totalCount]="post.reactionCount"
            [myReaction]="post.myReaction"
            [counts]="post.reactionCounts"
            (summaryChanged)="applySummary($event)"
          />
          <a class="comments-link" [routerLink]="['/posts', post.id]" fragment="comments">
            <app-icon name="message" [size]="17" />
            <span>{{ post.commentCount }}</span>
            <span class="visually-hidden">comments</span>
          </a>
        </div>
      </div>
    </article>
  `,
  styleUrl: './post-card.css',
})
export class PostCard {
  readonly post = input.required<ImagePost>();

  protected readonly api = inject(PostApiService);
  protected readonly broken = signal(false);

  /** Local overrides applied after reaction changes (no list reload needed). */
  private readonly reactionOverride = signal<ReactionSummary | null>(null);

  protected readonly displayPost = computed<ImagePost>(() => {
    const post = this.post();
    const override = this.reactionOverride();
    if (!override) {
      return post;
    }

    return {
      ...post,
      reactionCount: override.totalCount,
      reactionCounts: override.counts,
      myReaction: override.myReaction,
    };
  });

  protected applySummary(summary: ReactionSummary): void {
    this.reactionOverride.set(summary);
  }
}
