import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource, takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Params, Router, RouterLink } from '@angular/router';
import { debounceTime, distinctUntilChanged, map } from 'rxjs';
import { errorMessage } from '../../core/http/api-error';
import { ImageApiService } from '../../core/images/image-api.service';
import { ImageDto } from '../../core/models/api.models';
import { NotificationService } from '../../core/notifications/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { Pagination } from '../../shared/components/pagination/pagination';
import { PAGE_SIZE, SORT_OPTIONS, queryFromParams, sortValue } from './gallery-query';
import { ImageCard } from './image-card';
import { ImageLightbox } from '../../shared/components/image-lightbox/image-lightbox';
import { Icon } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-gallery-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ImageCard,
    Pagination,
    EmptyState,
    Icon,
    ImageLightbox,
  ],
  templateUrl: './gallery.page.html',
  styleUrl: './gallery.page.css',
})
export class GalleryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly api = inject(ImageApiService);
  protected readonly viewingIndex = signal<number | null>(null);
  protected readonly viewing = computed(() => {
    const index = this.viewingIndex();
    return index !== null && this.images.hasValue()
      ? (this.images.value().items[index] ?? null)
      : null;
  });
  private readonly confirm = inject(ConfirmService);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sortOptions = SORT_OPTIONS;
  protected readonly skeletons = Array.from({ length: PAGE_SIZE / 2 }, (_, index) => index);

  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });
  protected readonly query = computed(() => queryFromParams(this.queryParams()));
  protected readonly currentSort = computed(() => sortValue(this.query()));

  /** Re-fetches automatically whenever the URL-derived query changes. */
  protected readonly images = rxResource({
    params: () => this.query(),
    stream: ({ params }) => this.api.list(params),
  });

  protected readonly searchControl = new FormControl(this.query().search ?? '', {
    nonNullable: true,
  });

  constructor() {
    this.searchControl.valueChanges
      .pipe(
        map((value) => value.trim()),
        debounceTime(350),
        distinctUntilChanged(),
        takeUntilDestroyed(),
      )
      .subscribe((search) => this.updateQuery({ q: search || null, page: null }));

    // Keep the search box in sync when the URL changes (e.g. browser back button).
    effect(() => {
      const search = this.query().search ?? '';
      if (search !== this.searchControl.value.trim()) {
        this.searchControl.setValue(search, { emitEvent: false });
      }
    });
  }

  protected changeSort(value: string): void {
    this.updateQuery({ sort: value === SORT_OPTIONS[0].value ? null : value, page: null });
  }

  protected filterByTag(tag: string | null): void {
    this.updateQuery({ tag, page: null });
  }

  protected goToPage(page: number): void {
    this.updateQuery({ page: page > 1 ? page : null });
    window.scrollTo({
      top: 0,
      behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
    });
  }

  protected clearFilters(): void {
    this.searchControl.setValue('', { emitEvent: false });
    this.updateQuery({ q: null, tag: null, page: null });
  }

  protected async confirmDelete(image: ImageDto): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: 'Delete image?',
      message: `"${image.title}" will be permanently deleted. This cannot be undone.`,
      confirmText: 'Delete',
      danger: true,
    });
    if (!confirmed) {
      return;
    }

    this.api
      .delete(image.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.notifications.success(`"${image.title}" was deleted.`);
          const current = this.images.hasValue() ? this.images.value() : null;
          if (current && current.items.length === 1 && current.page > 1) {
            this.goToPage(current.page - 1); // The page would otherwise be empty.
          } else {
            this.images.reload();
          }
        },
        error: (error: unknown) => this.notifications.error(errorMessage(error)),
      });
  }

  private updateQuery(changes: Params): void {
    this.viewingIndex.set(null);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: changes,
      queryParamsHandling: 'merge',
    });
  }
}
