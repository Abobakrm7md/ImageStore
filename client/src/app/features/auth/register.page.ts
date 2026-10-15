import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { toApiError } from '../../core/http/api-error';
import { NotificationService } from '../../core/notifications/notification.service';
import { FieldError } from '../../shared/components/field-error/field-error';
import {
  applyServerErrors,
  matchesValidator,
  passwordStrengthValidator,
} from '../../shared/utils/forms';
import { Icon } from '../../shared/components/icon/icon';

@Component({
  selector: 'app-register-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, FieldError, Icon],
  templateUrl: './register.page.html',
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    password: [
      '',
      [
        Validators.required,
        Validators.minLength(8),
        Validators.maxLength(128),
        passwordStrengthValidator,
      ],
    ],
    confirmPassword: ['', [Validators.required]],
  });

  constructor() {
    const { password, confirmPassword } = this.form.controls;
    confirmPassword.addValidators(matchesValidator(() => password));
    password.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => confirmPassword.updateValueAndValidity({ onlySelf: true }));
  }

  protected submit(): void {
    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();
    this.submitting.set(true);
    this.auth
      .register({ email, password })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.notifications.success('Your account is ready. Start by uploading some images!');
          void this.router.navigateByUrl('/images');
        },
        error: (error: unknown) => {
          this.submitting.set(false);
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
}
