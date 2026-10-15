import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';

@Component({
  selector: 'app-not-found-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, EmptyState],
  template: `
    <section class="page">
      <app-empty-state
        heading="Page not found"
        message="The page you are looking for does not exist."
        icon="compass"
      >
        @if (auth.isAuthenticated()) {
          <a class="btn btn-primary" routerLink="/images">Go to my images</a>
        } @else {
          <a class="btn btn-primary" routerLink="/login">Sign in</a>
        }
      </app-empty-state>
    </section>
  `,
})
export class NotFoundPage {
  protected readonly auth = inject(AuthService);
}
