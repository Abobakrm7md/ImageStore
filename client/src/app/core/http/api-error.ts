import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '../models/api.models';

/** A normalized API error, derived from Problem Details when available. */
export interface ApiError {
  status: number;
  title: string;
  detail?: string;
  /** Field errors keyed by camelCase field name (e.g. `title`, `files[0]`). */
  fieldErrors: Record<string, string[]>;
  code?: string;
}

const DEFAULT_TITLES: Record<number, string> = {
  0: 'Network error',
  400: 'The request is invalid',
  401: 'Please sign in',
  403: 'Access denied',
  404: 'Not found',
  409: 'Conflict',
  413: 'The upload is too large',
  415: 'Unsupported content',
  429: 'Too many requests',
  500: 'Something went wrong',
};

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      status: -1,
      title: 'Unexpected error',
      detail: error instanceof Error ? error.message : undefined,
      fieldErrors: {},
    };
  }

  if (error.status === 0) {
    return {
      status: 0,
      title: DEFAULT_TITLES[0],
      detail: 'The server could not be reached. Check your connection and try again.',
      fieldErrors: {},
    };
  }

  const body = isProblemDetails(error.error) ? error.error : {};
  return {
    status: error.status,
    title: body.title ?? DEFAULT_TITLES[error.status] ?? `Request failed (${error.status})`,
    detail: body.detail,
    fieldErrors: body.errors ?? {},
    code: body.code,
  };
}

/** A single human-readable sentence describing the error. */
export function errorMessage(error: unknown): string {
  const apiError = toApiError(error);
  const firstFieldError = Object.values(apiError.fieldErrors).flat()[0];
  return firstFieldError ?? apiError.detail ?? apiError.title;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'status' in value || 'errors' in value)
  );
}
