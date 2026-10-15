import { ParamMap } from '@angular/router';
import { ImageListQuery, ImageSortField, SortDirection } from '../../core/models/api.models';

export const PAGE_SIZE = 12;

export interface SortOption {
  value: string;
  label: string;
  sortBy: ImageSortField;
  sortDirection: SortDirection;
}

export const SORT_OPTIONS: readonly SortOption[] = [
  { value: 'newest', label: 'Newest first', sortBy: 'createdAt', sortDirection: 'desc' },
  { value: 'oldest', label: 'Oldest first', sortBy: 'createdAt', sortDirection: 'asc' },
  { value: 'title', label: 'Title A–Z', sortBy: 'title', sortDirection: 'asc' },
  { value: 'title-desc', label: 'Title Z–A', sortBy: 'title', sortDirection: 'desc' },
  { value: 'largest', label: 'Largest first', sortBy: 'fileSize', sortDirection: 'desc' },
  { value: 'smallest', label: 'Smallest first', sortBy: 'fileSize', sortDirection: 'asc' },
];

/** The gallery state lives in the URL (?page=&q=&sort=&tag=) so it survives reloads and back/forward. */
export function queryFromParams(params: ParamMap): ImageListQuery {
  const sort =
    SORT_OPTIONS.find((option) => option.value === params.get('sort')) ?? SORT_OPTIONS[0];
  const page = Number.parseInt(params.get('page') ?? '1', 10);
  return {
    page: Number.isFinite(page) && page > 0 ? page : 1,
    pageSize: PAGE_SIZE,
    search: params.get('q')?.trim() || undefined,
    tag: params.get('tag')?.trim() || undefined,
    sortBy: sort.sortBy,
    sortDirection: sort.sortDirection,
  };
}

export function sortValue(query: ImageListQuery): string {
  return (
    SORT_OPTIONS.find(
      (option) => option.sortBy === query.sortBy && option.sortDirection === query.sortDirection,
    )?.value ?? SORT_OPTIONS[0].value
  );
}
