import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Parses "travel, Summer,  beach" into distinct, trimmed tags (case-insensitive). */
export function parseTags(value: string | null | undefined): string[] {
  const seen = new Set<string>();
  const tags: string[] = [];
  for (const raw of (value ?? '').split(',')) {
    const tag = raw.trim().replace(/\s+/g, ' ');
    const key = tag.toLocaleUpperCase();
    if (tag && !seen.has(key)) {
      seen.add(key);
      tags.push(tag);
    }
  }

  return tags;
}

/** Validates a comma-separated tag list against the server's limits. */
export function tagsValidator(maxTags: number, maxLength: number): ValidatorFn {
  return (control: AbstractControl<string>): ValidationErrors | null => {
    const tags = parseTags(control.value);
    if (tags.length > maxTags) {
      return { tooManyTags: { max: maxTags, actual: tags.length } };
    }

    const tooLong = tags.find((tag) => tag.length > maxLength);
    return tooLong ? { tagTooLong: { max: maxLength, tag: tooLong } } : null;
  };
}
