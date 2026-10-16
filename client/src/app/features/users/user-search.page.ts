import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { UserSummary } from '../../core/models/api.models';
import { SocialApiService } from '../../core/social/social-api.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { FieldError } from '../../shared/components/field-error/field-error';
import { Icon } from '../../shared/components/icon/icon';
import { Pagination } from '../../shared/components/pagination/pagination';
import { UserRow } from '../../shared/components/user-row/user-row';

export const USER_SEARCH_PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

@Component({
  selector: 'app-user-search-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, ReactiveFormsModule, EmptyState, FieldError, Icon, Pagination, UserRow],
  templateUrl: './user-search.page.html',
  styleUrl: './user-search.page.css',
})
export class UserSearchPage {
  private readonly social = inject(SocialApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);

  protected readonly currentUserId = computed(() => this.auth.user()?.id ?? '');

  protected readonly searchControl = this.fb.control('', {
    validators: [Validators.maxLength(100)],
  });

  private readonly queryParams = toSignal(
    this.route.queryParamMap.pipe(debounceTime(0), distinctUntilChanged()),
    { initialValue: null },
  );

  protected readonly term = computed(() => this.queryParams()?.get('q') ?? '');
  protected readonly page = computed(() => Number(this.queryParams()?.get('page') ?? '1') || 1);

  protected readonly results = rxResource({
    params: () => ({ term: this.term(), page: this.page() }),
    stream: ({ params }) =>
      this.social.search({
        search: params.term === '' ? undefined : params.term,
        page: params.page,
        pageSize: USER_SEARCH_PAGE_SIZE,
      }),
  });

  protected readonly error = computed(() => {
    const error = this.results.error();
    return error instanceof HttpErrorResponse ? error : null;
  });

  protected readonly invalid = computed(
    () => this.error()?.status === 400 && this.term().trim() === '',
  );

  constructor() {
    // Keep the input in sync when the URL changes (back/forward, deep links, clear button).
    effect(() => {
      const term = this.term();
      if (this.searchControl.value !== term) {
        this.searchControl.setValue(term, { emitEvent: false });
      }
    });

    // Debounce keystrokes into the URL; the URL is the single source of truth for the query.
    this.searchControl.valueChanges
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => {
        const term = value.trim();
        this.router.navigate([], {
          relativeTo: this.route,
          queryParams: { q: term === '' ? null : term, page: null },
        });
      });
  }

  protected submitSearch(): void {
    const term = this.searchControl.value.trim();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: term === '' ? null : term, page: null },
    });
  }

  protected clearSearch(): void {
    this.searchControl.setValue('');
  }

  protected goToPage(next: number): void {
    const term = this.term();
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { q: term === '' ? null : term, page: next },
    });
  }

  protected onFollowChanged(user: UserSummary, following: boolean): void {
    const result = this.results.value();
    if (!result) return;
    this.results.set({
      ...result,
      items: result.items.map((item) =>
        item.id === user.id ? { ...item, isFollowing: following } : item,
      ),
    });
  }


}
