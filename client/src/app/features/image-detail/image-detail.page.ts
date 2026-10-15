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
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorMessage, toApiError } from '../../core/http/api-error';
import { ImageApiService } from '../../core/images/image-api.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { FieldError } from '../../shared/components/field-error/field-error';
import { ImageLightbox } from '../../shared/components/image-lightbox/image-lightbox';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { applyServerErrors } from '../../shared/utils/forms';
import { parseTags, tagsValidator } from '../../shared/utils/tags';
import { Icon } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-image-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    FileSizePipe,
    FieldError,
    EmptyState,
    ImageLightbox,
    Icon,
  ],
  templateUrl: './image-detail.page.html',
  styleUrl: './image-detail.page.css',
})
export class ImageDetailPage {
  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  private readonly api = inject(ImageApiService);
  private readonly router = inject(Router);
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly image = rxResource({
    params: () => this.id(),
    stream: ({ params }) => this.api.get(params),
  });

  protected readonly notFound = computed(() => {
    const error = this.image.error();
    return error instanceof HttpErrorResponse && error.status === 404;
  });

  protected readonly contentUrl = computed(() => this.api.contentUrl(this.id()));
  protected readonly downloadUrl = computed(() => this.api.downloadUrl(this.id()));
  protected readonly lightboxOpen = signal(false);
  protected readonly saving = signal(false);
  protected readonly deleting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    title: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(2000)]],
    tags: ['', [tagsValidator(20, 50)]],
  });

  constructor() {
    // (Re)initialize the form whenever the image is loaded or replaced after saving.
    effect(() => {
      if (this.image.hasValue()) {
        const image = this.image.value();
        this.form.reset({
          title: image.title,
          description: image.description ?? '',
          tags: image.tags.join(', '),
        });
      }
    });
  }

  protected save(): void {
    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { title, description, tags } = this.form.getRawValue();
    this.saving.set(true);
    this.api
      .update(this.id(), {
        title: title.trim(),
        description: description.trim() || null,
        tags: parseTags(tags),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (updated) => {
          this.saving.set(false);
          this.image.set(updated);
          this.notifications.success('Changes saved.');
        },
        error: (error: unknown) => {
          this.saving.set(false);
          const apiError = toApiError(error);
          const unmatched = applyServerErrors(this.form, apiError);
          this.formError.set(
            unmatched[0] ??
              (Object.keys(apiError.fieldErrors).length
                ? null
                : (apiError.detail ?? apiError.title)),
          );
        },
      });
  }

  protected discardChanges(): void {
    if (this.image.hasValue()) {
      const image = this.image.value();
      this.form.reset({
        title: image.title,
        description: image.description ?? '',
        tags: image.tags.join(', '),
      });
      this.formError.set(null);
    }
  }

  protected async delete(): Promise<void> {
    const title = this.image.hasValue() ? this.image.value().title : 'this image';
    const confirmed = await this.confirm.confirm({
      title: 'Delete image?',
      message: `"${title}" will be permanently deleted. This cannot be undone.`,
      confirmText: 'Delete',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.deleting.set(true);
    this.api
      .delete(this.id())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.notifications.success(`"${title}" was deleted.`);
          void this.router.navigateByUrl('/images');
        },
        error: (error: unknown) => {
          this.deleting.set(false);
          this.notifications.error(errorMessage(error));
        },
      });
  }
}
