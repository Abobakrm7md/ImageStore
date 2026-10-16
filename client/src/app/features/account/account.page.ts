import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../core/http/api-error';
import { SocialApiService } from '../../core/social/social-api.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { Avatar } from '../../shared/components/avatar/avatar';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { FieldError } from '../../shared/components/field-error/field-error';
import { Icon } from '../../shared/components/icon/icon';

/** Mirrors the server-side profile picture limits (see ProfilePicture rules in the API). */
const MAX_PICTURE_BYTES = 2 * 1024 * 1024;

@Component({
  selector: 'app-account-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, Avatar, EmptyState, FieldError, Icon],
  templateUrl: './account.page.html',
  styleUrl: './account.page.css',
})
export class AccountPage {
  private readonly social = inject(SocialApiService);
  private readonly notifications = inject(NotificationService);
  private readonly confirm = inject(ConfirmService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly profile = rxResource({ stream: () => this.social.myProfile() });

  protected readonly form = this.fb.group({
    displayName: this.fb.control('', { validators: [Validators.required, Validators.maxLength(100)] }),
    bio: this.fb.control('', { validators: [Validators.maxLength(500)] }),
  });

  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);
  protected readonly uploadingPicture = signal(false);
  protected readonly pictureError = signal<string | null>(null);
  protected readonly pictureInput =
    viewChild<ElementRef<HTMLInputElement>>('pictureInput');

  protected readonly person = computed(() => this.profile.value());

  constructor() {
    // Seed the form once the profile arrives.
    effect(() => {
      const person = this.person();
      if (person) {
        this.form.patchValue(
          { displayName: person.displayName, bio: person.bio ?? '' },
          { emitEvent: false },
        );
      }
    });
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    try {
      const { displayName, bio } = this.form.getRawValue();
      const updated = await firstValueFrom(
        this.social.updateMyProfile({ displayName, bio: bio.trim() === '' ? null : bio.trim() }),
      );
      this.profile.set(updated);
      this.form.markAsPristine();
      this.notifications.success('Profile updated.');
    } catch (error) {
      this.applyError(error);
    } finally {
      this.saving.set(false);
    }
  }

  protected pickPicture(): void {
    this.pictureInput()?.nativeElement.click();
  }

  protected async onPictureSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow re-selecting the same file
    if (!file) {
      return;
    }

    this.pictureError.set(null);
    if (!file.type.startsWith('image/')) {
      this.pictureError.set('Please choose an image file (jpg, png, webp or gif).');
      return;
    }
    if (file.size > MAX_PICTURE_BYTES) {
      this.pictureError.set(`The picture is too large; the maximum is ${MAX_PICTURE_BYTES / 1024 / 1024} MB.`);
      return;
    }

    this.uploadingPicture.set(true);
    try {
      const updated = await firstValueFrom(this.social.setProfilePicture(file));
      this.profile.set(updated);
      this.notifications.success('Profile picture updated.');
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        this.pictureError.set(errorMessage(error));
      } else {
        this.pictureError.set('Could not upload the picture. Please try again.');
      }
    } finally {
      this.uploadingPicture.set(false);
    }
  }

  protected async removePicture(): Promise<void> {
    const confirmed = await this.confirm.confirm({
      title: 'Remove profile picture?',
      message: 'Your account will fall back to the generated initials avatar.',
      confirmText: 'Remove',
    });
    if (!confirmed) {
      return;
    }

    this.uploadingPicture.set(true);
    this.pictureError.set(null);
    try {
      await firstValueFrom(this.social.removeProfilePicture());
      this.profile.reload();
      this.notifications.success('Profile picture removed.');
    } catch (error) {
      if (error instanceof HttpErrorResponse) {
        this.pictureError.set(errorMessage(error));
      } else {
        this.pictureError.set('Could not remove the picture. Please try again.');
      }
    } finally {
      this.uploadingPicture.set(false);
    }
  }

  private applyError(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      const problem = error.error as { errors?: Record<string, string[]> } | null;
      if (problem?.errors) {
        let applied = false;
        for (const [key, messages] of Object.entries(problem.errors)) {
          const control = this.form.controls[key as keyof typeof this.form.controls];
          if (control) {
            control.setErrors({ server: messages[0] ?? 'Invalid value.' });
            applied = true;
          }
        }
        if (applied) {
          return;
        }
      }
    }
    this.saveError.set(
      error instanceof HttpErrorResponse ? errorMessage(error) : 'Could not save your profile.',
    );
  }
}
