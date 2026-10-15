import { FormControl, FormGroup } from '@angular/forms';
import { UploadPolicy } from '../../core/models/api.models';
import { fileExtension, titleFromFileName, validateImageFile } from './file-validation';
import { formatFileSize } from './format';
import { applyServerErrors, matchesValidator, passwordStrengthValidator } from './forms';
import { parseTags, tagsValidator } from './tags';

const policy: UploadPolicy = {
  maxFileSizeBytes: 1024 * 1024,
  maxFilesPerRequest: 10,
  allowedExtensions: ['.jpg', '.jpeg', '.png', '.webp'],
  allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
  titleMaxLength: 200,
  descriptionMaxLength: 2000,
  maxTagsPerImage: 20,
  tagMaxLength: 50,
};

const file = (name: string, size: number, type: string) =>
  new File([new Uint8Array(size)], name, { type });

describe('formatFileSize', () => {
  it.each([
    [0, '0 bytes'],
    [1, '1 byte'],
    [1023, '1023 bytes'],
    [1536, '1.5 KB'],
    [10 * 1024 * 1024, '10 MB'],
    [-1, '—'],
    [null, '—'],
  ])('formats %s as %s', (bytes, expected) => {
    expect(formatFileSize(bytes)).toBe(expected);
  });
});

describe('validateImageFile', () => {
  it('accepts supported images within the size limit', () => {
    expect(validateImageFile(file('cat.JPG', 100, 'image/jpeg'), policy)).toBeNull();
  });

  it('rejects unsupported extensions and MIME types', () => {
    expect(validateImageFile(file('anim.gif', 100, 'image/gif'), policy)).toContain(
      'Unsupported file type',
    );
    expect(validateImageFile(file('fake.png', 100, 'text/html'), policy)).toContain('text/html');
    expect(validateImageFile(file('noextension', 100, ''), policy)).toContain(
      'Unsupported file type',
    );
  });

  it('rejects empty and oversized files', () => {
    expect(validateImageFile(file('empty.png', 0, 'image/png'), policy)).toBe('The file is empty.');
    expect(
      validateImageFile(file('big.png', policy.maxFileSizeBytes + 1, 'image/png'), policy),
    ).toContain('maximum');
  });

  it('derives extensions and titles from file names', () => {
    expect(fileExtension('Holiday.Photo.WEBP')).toBe('.webp');
    expect(fileExtension('.hidden')).toBe('');
    expect(titleFromFileName('summer trip.jpg', 200)).toBe('summer trip');
    expect(titleFromFileName(' .jpg', 200)).toBe('Untitled image');
    // Dotfiles have no extension (and are rejected by validateImageFile, as on the server).
    expect(fileExtension('.png')).toBe('');
    expect(titleFromFileName('x'.repeat(300) + '.png', 200)).toHaveLength(200);
  });
});

describe('tags', () => {
  it('parses, trims, collapses whitespace and removes case-insensitive duplicates', () => {
    expect(parseTags(' Travel,  summer   trip ,travel,, ')).toEqual(['Travel', 'summer trip']);
    expect(parseTags('')).toEqual([]);
    expect(parseTags(null)).toEqual([]);
  });

  it('validates the number and length of tags', () => {
    const validator = tagsValidator(2, 5);
    expect(validator(new FormControl('a, b'))).toBeNull();
    expect(validator(new FormControl('a, b, c'))).toEqual({ tooManyTags: { max: 2, actual: 3 } });
    expect(validator(new FormControl('toolong'))).toEqual({
      tagTooLong: { max: 5, tag: 'toolong' },
    });
  });
});

describe('form helpers', () => {
  it('requires upper-case, lower-case and digits in passwords', () => {
    expect(passwordStrengthValidator(new FormControl('Passw0rd'))).toBeNull();
    expect(passwordStrengthValidator(new FormControl('password1'))).toEqual({
      passwordStrength: true,
    });
    expect(passwordStrengthValidator(new FormControl(''))).toBeNull();
  });

  it('checks that two controls match', () => {
    const password = new FormControl('Secret123');
    const validator = matchesValidator(() => password);
    expect(validator(new FormControl('Secret123'))).toBeNull();
    expect(validator(new FormControl('Other123'))).toEqual({ passwordMismatch: true });
  });

  it('maps server field errors onto controls and returns the rest', () => {
    const form = new FormGroup({ email: new FormControl(''), password: new FormControl('') });

    const unmatched = applyServerErrors(form, {
      status: 400,
      title: 'Invalid',
      fieldErrors: { email: ['Already taken.'], '': ['General problem.'] },
    });

    expect(form.controls.email.errors).toEqual({ server: 'Already taken.' });
    expect(form.controls.email.touched).toBe(true);
    expect(unmatched).toEqual(['General problem.']);
  });
});
