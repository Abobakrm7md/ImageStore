import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

export type PageItem = number | 'gap';

/** Page numbers to display: first, last, and a window around the current page. */
export function buildPageItems(page: number, totalPages: number, window = 1): PageItem[] {
  if (totalPages <= 1) {
    return totalPages === 1 ? [1] : [];
  }

  const pages = new Set<number>([1, totalPages]);
  for (let candidate = page - window; candidate <= page + window; candidate++) {
    if (candidate >= 1 && candidate <= totalPages) {
      pages.add(candidate);
    }
  }

  const sorted = [...pages].sort((a, b) => a - b);
  const items: PageItem[] = [];
  sorted.forEach((value, index) => {
    if (index > 0 && value - sorted[index - 1] > 1) {
      items.push('gap');
    }
    items.push(value);
  });
  return items;
}

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (totalPages() > 1) {
      <nav class="pagination" aria-label="Pagination">
        <button
          type="button"
          class="btn btn-ghost"
          aria-label="Previous page"
          [disabled]="page() <= 1"
          (click)="pageChange.emit(page() - 1)"
        >
          <span aria-hidden="true">‹</span> <span class="label">Previous</span>
        </button>
        <ol>
          @for (item of items(); track $index) {
            <li>
              @if (item === 'gap') {
                <span class="gap" aria-hidden="true">…</span>
              } @else {
                <button
                  type="button"
                  class="page-button"
                  [class.active]="item === page()"
                  [attr.aria-current]="item === page() ? 'page' : null"
                  [attr.aria-label]="'Page ' + item"
                  (click)="pageChange.emit(item)"
                >
                  {{ item }}
                </button>
              }
            </li>
          }
        </ol>
        <button
          type="button"
          class="btn btn-ghost"
          aria-label="Next page"
          [disabled]="page() >= totalPages()"
          (click)="pageChange.emit(page() + 1)"
        >
          <span class="label">Next</span> <span aria-hidden="true">›</span>
        </button>
      </nav>
    }
  `,
  styleUrl: './pagination.css',
})
export class Pagination {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly items = computed(() => buildPageItems(this.page(), this.totalPages()));
}
