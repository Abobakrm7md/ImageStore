import { UploadPolicy } from '../../core/models/api.models';
import { formatFileSize } from './format';

/**
 * Client-side pre-checks for a better user experience. They mirror the server's limits but are
 * not a security control: the server re-validates size and inspects the file signature.
 */
export function validateImageFile(file: File, policy: UploadPolicy): string | null {
  const extension = fileExtension(file.name);
  if (!extension || !policy.allowedExtensions.includes(extension)) {
    return `Unsupported file type. Allowed: ${policy.allowedExtensions.join(', ')}.`;
  }

  if (file.type && !policy.allowedContentTypes.includes(file.type)) {
    return `Unsupported file type (${file.type}).`;
  }

  if (file.size === 0) {
    return 'The file is empty.';
  }

  if (file.size > policy.maxFileSizeBytes) {
    return `The file is ${formatFileSize(file.size)}; the maximum is ${formatFileSize(policy.maxFileSizeBytes)}.`;
  }

  return null;
}

export function fileExtension(fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  return dot > 0 && dot < fileName.length - 1 ? fileName.slice(dot).toLowerCase() : '';
}

/** Default title for an upload: the file name without its extension. */
export function titleFromFileName(fileName: string, maxLength: number): string {
  const extension = fileExtension(fileName);
  const base = (extension ? fileName.slice(0, -extension.length) : fileName).trim();
  return (base || 'Untitled image').slice(0, maxLength);
}
