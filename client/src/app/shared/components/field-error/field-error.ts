import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, ValidationErrors } from '@angular/forms';
import { map, startWith, switchMap } from 'rxjs';

interface ControlState {
  errors: ValidationErrors | null;
  visible: boolean;
}

/** Shows the first validation message of a control once the user has interacted with it. */
@Component({
  selector: 'app-field-error',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (message(); as text) {
      <p class="field-error" [id]="id()" role="alert">{{ text }}</p>
    }
  `,
})
export class FieldError {
  readonly control = input.required<AbstractControl>();
  readonly label = input('This field');
  readonly id = input<string>();

  // Control.events emits on value, status, touched and pristine changes (incl. markAllAsTouched).
  private readonly state = toSignal(
    toObservable(this.control).pipe(
      switchMap((control) =>
        control.events.pipe(
          startWith(null),
          map((): ControlState => ({
            errors: control.errors,
            visible: control.touched || control.dirty,
          })),
        ),
      ),
    ),
    { initialValue: { errors: null, visible: false } satisfies ControlState },
  );

  protected readonly message = computed(() => {
    const { errors, visible } = this.state();
    return errors && visible ? describeError(this.label(), errors) : null;
  });
}

export function describeError(label: string, errors: ValidationErrors): string {
  const [key, value] = Object.entries(errors)[0];
  const details = (value ?? {}) as Record<string, unknown>;
  switch (key) {
    case 'required':
      return `${label} is required.`;
    case 'email':
      return 'Enter a valid email address.';
    case 'minlength':
      return `${label} must be at least ${details['requiredLength']} characters.`;
    case 'maxlength':
      return `${label} must be at most ${details['requiredLength']} characters.`;
    case 'passwordStrength':
      return 'Use upper- and lower-case letters and at least one digit.';
    case 'passwordMismatch':
      return 'The passwords do not match.';
    case 'tooManyTags':
      return `Use at most ${details['max']} tags.`;
    case 'tagTooLong':
      return `Each tag must be at most ${details['max']} characters.`;
    case 'server':
      return String(value);
    default:
      return `${label} is invalid.`;
  }
}
