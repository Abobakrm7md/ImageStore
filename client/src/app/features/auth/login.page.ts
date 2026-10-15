import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService, safeReturnUrl } from '../../core/auth/auth.service';
import { toApiError } from '../../core/http/api-error';
import { NotificationService } from '../../core/notifications/notification.service';
import { FieldError } from '../../shared/components/field-error/field-error';
import { applyServerErrors } from '../../shared/utils/forms';
import { Icon } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, FieldError, Icon],
  templateUrl: './login.page.html',
})
export class LoginPage {
  /** Bound from the ?returnUrl= query parameter. */
  readonly returnUrl = input<string>();

  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
    rememberMe: [false],
  });

  protected submit(): void {
    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.auth
      .login(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (user) => {
          this.notifications.success(`Welcome back, ${user.email}!`);
          void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
        },
        error: (error: unknown) => {
          this.submitting.set(false);
          const apiError = toApiError(error);
          const unmatched = applyServerErrors(this.form, apiError);
          this.formError.set(unmatched[0] ?? apiError.detail ?? apiError.title);
          this.form.controls.password.reset('');
        },
      });
  }
}
