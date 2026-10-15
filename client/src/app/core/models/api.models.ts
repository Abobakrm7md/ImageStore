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

/* ---------------------------------------------------------------------------
 * Social platform: profiles, following, posts, comments and reactions.
 * ------------------------------------------------------------------------ */

/** Stable reaction identifiers (camelCase names of the server enum). */
export type ReactionType = 'like' | 'love' | 'laugh' | 'wow' | 'sad' | 'dislike';

/** A user as rendered in lists (search results, followers, following, post authors). */
export interface UserSummary {
  id: string;
  userName: string;
  displayName: string;
  hasProfilePicture: boolean;
  /** Whether the signed-in user follows this user (viewer-relative). */
  isFollowing?: boolean;
}

/** A full user profile. */
export interface UserProfile extends UserSummary {
  bio: string | null;
  followerCount: number;
  followingCount: number;
  postCount: number;
  isFollowing: boolean;
  isCurrentUser: boolean;
  createdAtUtc: string;
}

export interface UpdateMyProfileRequest {
  displayName: string;
  bio: string | null;
}

/** The published image of a post; bytes come from `/api/posts/{id}/content`. */
export interface PostImage {
  imageId: string;
  contentType: string;
  width: number | null;
  height: number | null;
}

export interface ReactionCount {
  reaction: ReactionType;
  count: number;
}

/** An image post with everything needed to render a feed card. */
export interface ImagePost {
  id: string;
  title: string;
  description: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  author: UserSummary;
  image: PostImage;
  commentCount: number;
  reactionCount: number;
  /** One entry per known reaction type, zero counts included. */
  reactionCounts: ReactionCount[];
  myReaction: ReactionType | null;
  isMine: boolean;
}

export interface CreatePostRequest {
  imageId: string;
  title?: string | null;
  description?: string | null;
}

export interface UpdatePostRequest {
  title: string;
  description: string | null;
}

export interface CommentDto {
  id: string;
  postId: string;
  author: UserSummary;
  content: string;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  isMine: boolean;
  canDelete: boolean;
}

export interface ReactionSummary {
  totalCount: number;
  counts: ReactionCount[];
  myReaction: ReactionType | null;
}
