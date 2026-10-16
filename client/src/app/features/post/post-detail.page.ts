import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorMessage, toApiError } from '../../core/http/api-error';
import { NotificationService } from '../../core/notifications/notification.service';
import { PostApiService } from '../../core/posts/post-api.service';
import { CommentDto, ImagePost, ReactionSummary } from '../../core/models/api.models';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { Icon } from '../../shared/components/icon/icon';
import { Avatar } from '../../shared/components/avatar/avatar';
import { ImageLightbox } from '../../shared/components/image-lightbox/image-lightbox';
import { Pagination } from '../../shared/components/pagination/pagination';
import { ReactionBar } from '../../shared/components/reaction-bar/reaction-bar';
import { applyServerErrors } from '../../shared/utils/forms';

export const COMMENTS_PAGE_SIZE = 20;

@Component({
  selector: 'app-post-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    Avatar,
    EmptyState,
    Icon,
    ImageLightbox,
    Pagination,
    ReactionBar,
  ],
  templateUrl: './post-detail.page.html',
  styleUrl: './post-detail.page.css',
})
export class PostDetailPage {
  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  protected readonly api = inject(PostApiService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly lightboxOpen = signal(false);
  protected readonly broken = signal(false);

  protected readonly post = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.get(params),
  });

  protected readonly notFound = computed(() => {
    const error = this.post.error();
    return error instanceof HttpErrorResponse && error.status === 404;
  });

  protected readonly contentUrl = computed(() => this.api.contentUrl(this.id()));

  /* ---------------- comments ---------------- */

  protected readonly commentsPage = signal(1);
  protected readonly comments = rxResource({
    params: () => ({ postId: this.id(), page: this.commentsPage() }),
    stream: ({ params }) => this.api.comments(params.postId, params.page, COMMENTS_PAGE_SIZE),
  });

  protected readonly commentControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(2000)],
  });
  protected readonly sendingComment = signal(false);
  protected readonly editingCommentId = signal<string | null>(null);
  protected readonly editCommentControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(2000)],
  });

  /* ---------------- post management (author only) ---------------- */

  protected readonly editMode = signal(false);
  protected readonly savingPost = signal(false);
  protected readonly deletingPost = signal(false);
  protected readonly postFormError = signal<string | null>(null);

  protected readonly postForm = this.fb.group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(2000)]],
  });

  constructor() {
    effect(() => {
      if (this.post.hasValue() && this.editMode()) {
        const post = this.post.value();
        this.postForm.reset({ title: post.title, description: post.description ?? '' });
      }
    });
  }

  protected applySummary(summary: ReactionSummary): void {
    this.post.update((post) =>
      post
        ? {
            ...post,
            reactionCount: summary.totalCount,
            reactionCounts: summary.counts,
            myReaction: summary.myReaction,
          }
        : post,
    );
  }

  protected submitComment(): void {
    const content = this.commentControl.value.trim();
    if (content.length === 0 || this.sendingComment()) {
      this.commentControl.markAsTouched();
      return;
    }

    this.sendingComment.set(true);
    this.api
      .createComment(this.id(), content)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (comment) => {
          this.sendingComment.set(false);
          this.commentControl.reset('');
          // Append locally – the list refreshes without a full reload.
          this.comments.update((page) =>
            page
              ? { ...page, items: [...page.items, comment], totalCount: page.totalCount + 1 }
              : page,
          );
          this.syncPostCommentCount(1);
        },
        error: (error: unknown) => {
          this.sendingComment.set(false);
          this.notifications.error(errorMessage(error));
        },
      });
  }

  protected startEditComment(comment: CommentDto): void {
    this.editingCommentId.set(comment.id);
    this.editCommentControl.reset(comment.content);
  }

  protected cancelEditComment(): void {
    this.editingCommentId.set(null);
  }

  protected saveComment(comment: CommentDto): void {
    const content = this.editCommentControl.value.trim();
    if (content.length === 0) {
      this.editCommentControl.markAsTouched();
      return;
    }

    this.api
      .updateComment(this.id(), comment.id, content)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.editingCommentId.set(null);
          this.replaceComment(updated);
        },
        error: (error: unknown) => this.notifications.error(errorMessage(error)),
      });
  }

  protected async deleteComment(comment: CommentDto): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: 'Delete comment?',
      message: 'This comment will be permanently removed.',
      confirmText: 'Delete',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.api
      .deleteComment(this.id(), comment.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.comments.update((page) =>
            page
              ? {
                  ...page,
                  items: page.items.filter((item) => item.id !== comment.id),
                  totalCount: Math.max(0, page.totalCount - 1),
                }
              : page,
          );
          this.syncPostCommentCount(-1);
        },
        error: (error: unknown) => this.notifications.error(errorMessage(error)),
      });
  }

  protected goToCommentsPage(page: number): void {
    this.commentsPage.set(page);
  }

  protected toggleEditMode(): void {
    this.editMode.update((value) => !value);
    this.postFormError.set(null);
  }

  protected savePost(post: ImagePost): void {
    this.postFormError.set(null);
    if (this.postForm.invalid) {
      this.postForm.markAllAsTouched();
      return;
    }

    const { title, description } = this.postForm.getRawValue();
    this.savingPost.set(true);
    this.api
      .update(this.id(), { title: title.trim(), description: description.trim() || null })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.savingPost.set(false);
          this.editMode.set(false);
          this.post.set(updated);
          this.notifications.success('Post updated.');
        },
        error: (error: unknown) => {
          this.savingPost.set(false);
          const apiError = toApiError(error);
          const unmatched = applyServerErrors(this.postForm, apiError);
          this.postFormError.set(unmatched[0] ?? apiError.detail ?? apiError.title);
        },
      });
  }

  protected async deletePost(post: ImagePost): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: 'Delete post?',
      message: `"${post.title}" will be unpublished and its comments and reactions removed. The image itself stays in your gallery.`,
      confirmText: 'Delete',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.deletingPost.set(true);
    this.api
      .delete(this.id())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.notifications.success('Post deleted.');
          void this.router.navigateByUrl('/feed');
        },
        error: (error: unknown) => {
          this.deletingPost.set(false);
          this.notifications.error(errorMessage(error));
        },
      });
  }

  private replaceComment(updated: CommentDto): void {
    this.comments.update((page) =>
      page
        ? { ...page, items: page.items.map((item) => (item.id === updated.id ? updated : item)) }
        : page,
    );
  }

  private syncPostCommentCount(delta: number): void {
    this.post.update((post) =>
      post ? { ...post, commentCount: Math.max(0, post.commentCount + delta) } : post,
    );
  }
}
