/** Contracts of the ImageStore REST API (mirrors the server DTOs). */

export interface CurrentUser {
  id: string;
  email: string;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface RegisterRequest {
  email: string;
  password: string;
}

export interface ImageDto {
  id: string;
  title: string;
  description: string | null;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  width: number | null;
  height: number | null;
  tags: string[];
  /** ISO-8601 UTC timestamp. */
  createdAtUtc: string;
  updatedAtUtc: string | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export type ImageSortField = 'createdAt' | 'title' | 'fileSize';
export type SortDirection = 'asc' | 'desc';

export interface ImageListQuery {
  page: number;
  pageSize: number;
  search?: string;
  tag?: string;
  sortBy: ImageSortField;
  sortDirection: SortDirection;
}

export interface UpdateImageRequest {
  title: string;
  description: string | null;
  tags: string[];
}

export interface UploadMetadata {
  title?: string;
  description?: string;
  tags?: string[];
}

export interface UploadImagesResponse {
  items: ImageDto[];
}

export interface UploadPolicy {
  maxFileSizeBytes: number;
  maxFilesPerRequest: number;
  allowedExtensions: string[];
  allowedContentTypes: string[];
  titleMaxLength: number;
  descriptionMaxLength: number;
  maxTagsPerImage: number;
  tagMaxLength: number;
}

/** RFC 7807 Problem Details as returned by the API. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  code?: string;
  errors?: Record<string, string[]>;
}
