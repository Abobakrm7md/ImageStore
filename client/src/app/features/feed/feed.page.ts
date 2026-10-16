import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { rxResource, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PostApiService } from '../../core/posts/post-api.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { Pagination } from '../../shared/components/pagination/pagination';
import { PostCard } from '../../shared/components/post-card/post-card';
import { Icon } from '../../shared/components/icon/icon';

export const FEED_PAGE_SIZE = 10;

@Component({
  selector: 'app-feed-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, PostCard, Pagination, EmptyState, Icon],
  templateUrl: './feed.page.html',
  styleUrl: './feed.page.css',
})
export class FeedPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(PostApiService);

  protected readonly skeletons = Array.from({ length: 3 }, (_, index) => index);

  private readonly queryParams = toSignal(this.route.queryParamMap, { requireSync: true });

  /** The current page lives in the URL so reloads and back/forward keep the feed position. */
  protected readonly page = computed(() => {
    const parsed = Number.parseInt(this.queryParams()?.get('page') ?? '1', 10);
    return Number.isFinite(parsed) && parsed > 0 ? parsed : 1;
  });

  protected readonly feed = rxResource({
    params: () => this.page(),
    stream: ({ params }) => this.api.feed(params, FEED_PAGE_SIZE),
  });

  protected goToPage(page: number): void {
    void this.router.navigate(['/feed'], { queryParams: { page: page > 1 ? page : null } });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
