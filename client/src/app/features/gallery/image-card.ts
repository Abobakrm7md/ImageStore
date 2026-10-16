import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ImageApiService } from '../../core/images/image-api.service';
import { ImageDto } from '../../core/models/api.models';
import { FileSizePipe } from '../../shared/pipes/file-size.pipe';
import { Icon } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-image-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, FileSizePipe, Icon],
  template: `
    @let img = image();
    <article class="image-card card">
      <button
        type="button"
        class="thumb"
        (click)="previewRequested.emit()"
        [attr.aria-label]="'View ' + img.title"
      >
        @if (!broken()) {
          <img
            [src]="api.contentUrl(img.id)"
            [alt]="img.title"
            loading="lazy"
            [attr.width]="img.width || 1200"
            [attr.height]="img.height || 900"
            decoding="async"
            (error)="broken.set(true)"
          />
        } @else {
          <span class="thumb-fallback"><app-icon name="image" [size]="36" /></span>
        }
      </button>
      <div class="card-body">
        <h2 class="card-title" [title]="img.title">{{ img.title }}</h2>
        <p class="card-meta">
          <time [attr.datetime]="img.createdAtUtc">{{
            img.createdAtUtc | date: 'mediumDate'
          }}</time>
          <span aria-hidden="true">·</span>
          <span>{{ img.fileSizeBytes | fileSize }}</span>
        </p>
        @if (img.tags.length) {
          <ul class="tag-list" aria-label="Tags">
            @for (tag of img.tags; track tag) {
              <li>
                <button
                  type="button"
                  class="tag"
                  (click)="tagSelected.emit(tag)"
                  [attr.aria-label]="'Filter by tag ' + tag"
                >
                  #{{ tag }}
                </button>
              </li>
            }
          </ul>
        }
      </div>
      <div class="card-actions">
        <a
          class="btn btn-ghost btn-sm"
          [routerLink]="['/images', img.id]"
          [attr.aria-label]="'Edit ' + img.title"
          title="Edit"
        >
          <app-icon name="edit" [size]="16" /><span class="action-label">Edit</span>
        </a>
        <a
          class="btn btn-ghost btn-sm"
          [href]="api.downloadUrl(img.id)"
          download
          [attr.aria-label]="'Download ' + img.title"
          title="Download"
        >
          <app-icon name="download" [size]="16" /><span class="action-label">Download</span>
        </a>
        <button
          type="button"
          class="btn btn-ghost btn-sm btn-danger-text"
          (click)="deleteRequested.emit(img)"
          [attr.aria-label]="'Delete ' + img.title"
          title="Delete"
        >
          <app-icon name="trash" [size]="16" /><span class="action-label">Delete</span>
        </button>
      </div>
    </article>
  `,
})
export class ImageCard {
  readonly image = input.required<ImageDto>();
  readonly previewRequested = output<void>();
  readonly deleteRequested = output<ImageDto>();
  readonly tagSelected = output<string>();

  protected readonly api = inject(ImageApiService);
  protected readonly broken = signal(false);
}
