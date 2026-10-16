import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../core/http/api-error';
import { ImageApiService } from '../../core/images/image-api.service';
import { PostApiService } from '../../core/posts/post-api.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { FieldError } from '../../shared/components/field-error/field-error';
import { Icon } from '../../shared/components/icon/icon';
import { Pagination } from '../../shared/components/pagination/pagination';

export const PICKER_PAGE_SIZE = 12;

@Component({
  selector: 'app-new-post-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, EmptyState, FieldError, Icon, Pagination],
  templateUrl: './new-post.page.html',
  styleUrl: './new-post.page.css',
})
export class NewPostPage {
  protected readonly images = inject(ImageApiService);
  private readonly posts = inject(PostApiService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly page = signal(1);

  protected readonly library = rxResource({
    params: () => this.page(),
    stream: ({ params }) =>
      this.images.list({
        page: params,
        pageSize: PICKER_PAGE_SIZE,
        sortBy: 'createdAt',
        sortDirection: 'desc',
      }),
  });

  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => {
    const id = this.selectedId();
    return id ? (this.library.value()?.items.find((image) => image.id === id) ?? null) : null;
  });

  protected readonly form = this.fb.group({
    title: this.fb.control('', { validators: [Validators.maxLength(200)] }),
    description: this.fb.control('', { validators: [Validators.maxLength(2000)] }),
  });

  protected readonly pending = signal(false);
  protected readonly submitError = signal<string | null>(null);

  constructor() {
    // Prefill the form from the chosen image (title/description can still be edited).
    effect(() => {
      const selected = this.selected();
      if (selected) {
        this.form.patchValue(
          { title: selected.title, description: selected.description ?? '' },
          { emitEvent: false },
        );
        this.form.markAsPristine();
        this.submitError.set(null);
      }
    });
  }

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected goToPage(next: number): void {
    this.page.set(next);
    this.selectedId.set(null);
  }

  protected async submit(): Promise<void> {
    const selected = this.selected();
    if (!selected || this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }

    this.pending.set(true);
    this.submitError.set(null);
    try {
      const { title, description } = this.form.getRawValue();
      const post = await firstValueFrom(
        this.posts.create({
          imageId: selected.id,
          title: title.trim() === '' ? null : title.trim(),
          description: description.trim() === '' ? null : description.trim(),
        }),
      );
      this.notifications.success('Post published.');
      await this.router.navigate(['/posts', post.id]);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.submitError.set(
          'This image is already published as a post. Choose a different image, or edit the existing post.',
        );
      } else {
        this.submitError.set(
          error instanceof HttpErrorResponse ? errorMessage(error) : 'Could not publish the post.',
        );
      }
      this.pending.set(false);
    }
  }
}
