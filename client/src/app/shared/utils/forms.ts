import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';
import { ApiError } from '../../core/http/api-error';

/** Requires upper-case, lower-case and a digit (mirrors the server's Identity password policy). */
export const passwordStrengthValidator: ValidatorFn = (
  control: AbstractControl<string>,
): ValidationErrors | null => {
  const value = control.value ?? '';
  if (!value) {
    return null;
  }

  return /[a-z]/.test(value) && /[A-Z]/.test(value) && /\d/.test(value)
    ? null
    : { passwordStrength: true };
};

/**
 * Control validator: the value must equal another control's value (e.g. password confirmation).
 * Re-validate this control when the other one changes.
 */
export function matchesValidator(other: () => AbstractControl | null): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const otherValue = other()?.value;
    return control.value && otherValue !== control.value ? { passwordMismatch: true } : null;
  };
}

/**
 * Copies server-side field errors (Problem Details "errors") onto matching form controls.
 * Returns the messages that did not match any control, for display as a form-level error.
 */
export function applyServerErrors(form: FormGroup, error: ApiError): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(error.fieldErrors)) {
    const control = form.get(field);
    if (control && messages.length > 0) {
      control.setErrors({ ...(control.errors ?? {}), server: messages[0] });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }

  return unmatched;
}
