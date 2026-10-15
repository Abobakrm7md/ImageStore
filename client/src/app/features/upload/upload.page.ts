import { HttpEventType } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import {
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { errorMessage, toApiError } from '../../core/http/api-error';
import { ImageApiService } from '../../core/images/image-api.service';
import { ImageDto, UploadPolicy } from '../../core/models/api.models';
import { NotificationService } from '../../core/notifications/notification.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { FieldError } from '../../shared/components/field-error/field-error';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { titleFromFileName, validateImageFile } from '../../shared/utils/file-validation';
import { formatFileSize } from '../../shared/utils/format';
import { parseTags, tagsValidator } from '../../shared/utils/tags';
import { Icon } from '../../shared/components/icon/icon';

/** `rejected`: the server refused the file (validation); `failed`: transient error, can be retried. */
export type UploadStatus =
  'ready' | 'invalid' | 'queued' | 'uploading' | 'done' | 'rejected' | 'failed';

export interface UploadItem {
  readonly id: number;
  readonly file: File;
  readonly previewUrl: string | null;
  /** The browser could not decode the preview (the server will reject the file anyway). */
  readonly previewBroken: boolean;
  readonly title: FormControl<string>;
  readonly status: UploadStatus;
  readonly progress: number;
  readonly error: string | null;
  readonly result: ImageDto | null;
}

/** Parallel uploads; each file is its own request so progress and errors are reported per file. */
const CONCURRENCY = 2;
const MAX_QUEUE = 50;

@Component({
  selector: 'app-upload-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, FileSizePipe, FieldError, EmptyState, Icon],
  templateUrl: './upload.page.html',
  styleUrl: './upload.page.css',
})
export class UploadPage {
  private readonly api = inject(ImageApiService);
  private readonly notifications = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly subscriptions = new Map<number, Subscription>();
  private nextId = 1;
  private batchRunning = false;

  protected readonly policy = rxResource({ stream: () => this.api.uploadPolicy() });
  protected readonly items = signal<UploadItem[]>([]);
  protected readonly dragActive = signal(false);

  protected readonly counts = computed(() => {
    const items = this.items();
    const count = (status: UploadStatus) => items.filter((item) => item.status === status).length;
    return {
      ready: count('ready'),
      invalid: count('invalid'),
      active: count('queued') + count('uploading'),
      done: count('done'),
      rejected: count('rejected'),
      failed: count('failed'),
    };
  });

  protected readonly accept = computed(() =>
    this.policy.hasValue()
      ? [...this.policy.value().allowedExtensions, ...this.policy.value().allowedContentTypes].join(
          ',',
        )
      : 'image/*',
  );

  protected readonly metadataForm = this.formBuilder.group({
    description: [''],
    tags: [''],
  });

  constructor() {
    // Validation limits come from the server's upload policy (single source of truth).
    effect(() => {
      if (this.policy.hasValue()) {
        const policy = this.policy.value();
        const { description, tags } = this.metadataForm.controls;
        description.setValidators(Validators.maxLength(policy.descriptionMaxLength));
        tags.setValidators(tagsValidator(policy.maxTagsPerImage, policy.tagMaxLength));
        description.updateValueAndValidity();
        tags.updateValueAndValidity();
      }
    });

    inject(DestroyRef).onDestroy(() => {
      for (const subscription of this.subscriptions.values()) {
        subscription.unsubscribe(); // Aborts in-flight requests.
      }
      for (const item of this.items()) {
        this.revokePreview(item);
      }
    });
  }

  protected describeLimits(policy: UploadPolicy): string {
    const types = policy.allowedExtensions
      .map((extension) => extension.slice(1).toUpperCase())
      .join(', ');
    return `${types} · up to ${formatFileSize(policy.maxFileSizeBytes)} per file`;
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'copy';
    }
    this.dragActive.set(true);
  }

  protected onDragLeave(event: DragEvent): void {
    const related = event.relatedTarget as Node | null;
    if (!related || !(event.currentTarget as HTMLElement).contains(related)) {
      this.dragActive.set(false);
    }
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    this.addFiles(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onFilesSelected(input: HTMLInputElement): void {
    this.addFiles(Array.from(input.files ?? []));
    input.value = ''; // Allow selecting the same file again.
  }

  protected addFiles(files: File[]): void {
    if (!this.policy.hasValue() || files.length === 0) {
      return;
    }

    const policy = this.policy.value();
    const room = MAX_QUEUE - this.items().length;
    if (files.length > room) {
      this.notifications.info(
        `You can queue up to ${MAX_QUEUE} files at a time; ${files.length - Math.max(room, 0)} were skipped.`,
      );
    }

    const added = files.slice(0, Math.max(room, 0)).map((file): UploadItem => {
      const error = validateImageFile(file, policy);
      return {
        id: this.nextId++,
        file,
        previewUrl: error ? null : URL.createObjectURL(file),
        previewBroken: false,
        title: new FormControl(titleFromFileName(file.name, policy.titleMaxLength), {
          nonNullable: true,
          validators: [Validators.maxLength(policy.titleMaxLength)],
        }),
        status: error ? 'invalid' : 'ready',
        progress: 0,
        error,
        result: null,
      };
    });

    this.items.update((items) => [...items, ...added]);
  }

  protected markPreviewBroken(item: UploadItem): void {
    this.patch(item.id, { previewBroken: true });
  }

  protected uploadAll(): void {
    const ready = this.items().filter((item) => item.status === 'ready');
    if (this.metadataForm.invalid || ready.some((item) => item.title.invalid)) {
      this.metadataForm.markAllAsTouched();
      ready.forEach((item) => item.title.markAsTouched());
      return;
    }

    this.batchRunning = true;
    for (const item of ready) {
      this.patch(item.id, { status: 'queued', error: null });
    }
    this.pump();
  }

  protected retry(item: UploadItem): void {
    this.batchRunning = true;
    this.patch(item.id, { status: 'queued', error: null, progress: 0 });
    this.pump();
  }

  protected remove(item: UploadItem): void {
    this.subscriptions.get(item.id)?.unsubscribe();
    this.subscriptions.delete(item.id);
    this.revokePreview(item);
    this.items.update((items) => items.filter((candidate) => candidate.id !== item.id));
    this.pump();
  }

  protected clearFinished(): void {
    const finished = this.items().filter(
      (item) => item.status === 'done' || item.status === 'invalid' || item.status === 'rejected',
    );
    finished.forEach((item) => this.revokePreview(item));
    this.items.update((items) => items.filter((item) => !finished.includes(item)));
  }

  /** Starts queued uploads while fewer than CONCURRENCY are running. */
  private pump(): void {
    const items = this.items();
    let running = items.filter((item) => item.status === 'uploading').length;
    for (const item of items) {
      if (running >= CONCURRENCY) {
        break;
      }
      if (item.status === 'queued') {
        this.start(item);
        running++;
      }
    }

    if (
      this.batchRunning &&
      !this.items().some((item) => item.status === 'queued' || item.status === 'uploading')
    ) {
      this.batchRunning = false;
      this.announceSummary();
    }
  }

  private start(item: UploadItem): void {
    const { description, tags } = this.metadataForm.getRawValue();
    this.patch(item.id, { status: 'uploading', progress: 0 });

    const subscription = this.api
      .upload(item.file, {
        title: item.title.value.trim() || undefined,
        description: description.trim() || undefined,
        tags: parseTags(tags),
      })
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress) {
            const percent = event.total ? Math.round((100 * event.loaded) / event.total) : 0;
            this.patch(item.id, { progress: Math.min(percent, 99) }); // 100% once the server confirms.
          } else if (event.type === HttpEventType.Response) {
            this.patch(item.id, {
              status: 'done',
              progress: 100,
              result: event.body?.items[0] ?? null,
            });
          }
        },
        error: (error: unknown) => {
          const status = toApiError(error).status;
          const rejected = status === 400 || status === 413 || status === 415;
          this.patch(item.id, {
            status: rejected ? 'rejected' : 'failed',
            error: errorMessage(error),
          });
          this.finish(item.id);
        },
        complete: () => this.finish(item.id),
      });

    this.subscriptions.set(item.id, subscription);
  }

  private finish(id: number): void {
    this.subscriptions.delete(id);
    this.pump();
  }

  private announceSummary(): void {
    const { done, failed, rejected } = this.counts();
    const problems = failed + rejected;
    if (problems === 0 && done > 0) {
      this.notifications.success(done === 1 ? 'Image uploaded.' : `${done} images uploaded.`);
    } else if (problems > 0) {
      this.notifications.error(
        `${problems} ${problems === 1 ? 'file was' : 'files were'} not uploaded. Check the list for details.`,
      );
    }
  }

  private patch(id: number, changes: Partial<Omit<UploadItem, 'id' | 'file' | 'title'>>): void {
    this.items.update((items) =>
      items.map((item) => (item.id === id ? { ...item, ...changes } : item)),
    );
  }

  private revokePreview(item: UploadItem): void {
    if (item.previewUrl) {
      URL.revokeObjectURL(item.previewUrl);
    }
  }
}
