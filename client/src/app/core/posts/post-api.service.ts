import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CommentDto,
  CreatePostRequest,
  ImagePost,
  PagedResult,
  ReactionSummary,
  ReactionType,
  UpdatePostRequest,
} from '../models/api.models';

/** Typed client for posts, the feed, comments and reactions. */
@Injectable({ providedIn: 'root' })
export class PostApiService {
  private readonly http = inject(HttpClient);

  feed(page: number, pageSize: number): Observable<PagedResult<ImagePost>> {
    return this.http.get<PagedResult<ImagePost>>('/api/feed', { params: paging(page, pageSize) });
  }

  get(postId: string): Observable<ImagePost> {
    return this.http.get<ImagePost>(`/api/posts/${encodeURIComponent(postId)}`);
  }

  create(request: CreatePostRequest): Observable<ImagePost> {
    return this.http.post<ImagePost>('/api/posts', request);
  }

  update(postId: string, request: UpdatePostRequest): Observable<ImagePost> {
    return this.http.put<ImagePost>(`/api/posts/${encodeURIComponent(postId)}`, request);
  }

  delete(postId: string): Observable<void> {
    return this.http.delete<void>(`/api/posts/${encodeURIComponent(postId)}`);
  }

  comments(postId: string, page: number, pageSize: number): Observable<PagedResult<CommentDto>> {
    return this.http.get<PagedResult<CommentDto>>(
      `/api/posts/${encodeURIComponent(postId)}/comments`,
      { params: paging(page, pageSize) },
    );
  }

  createComment(postId: string, content: string): Observable<CommentDto> {
    return this.http.post<CommentDto>(`/api/posts/${encodeURIComponent(postId)}/comments`, { content });
  }

  updateComment(postId: string, commentId: string, content: string): Observable<CommentDto> {
    return this.http.put<CommentDto>(
      `/api/posts/${encodeURIComponent(postId)}/comments/${encodeURIComponent(commentId)}`,
      { content },
    );
  }

  deleteComment(postId: string, commentId: string): Observable<void> {
    return this.http.delete<void>(
      `/api/posts/${encodeURIComponent(postId)}/comments/${encodeURIComponent(commentId)}`,
    );
  }

  reactions(postId: string): Observable<ReactionSummary> {
    return this.http.get<ReactionSummary>(`/api/posts/${encodeURIComponent(postId)}/reactions`);
  }

  setReaction(postId: string, reaction: ReactionType): Observable<ReactionSummary> {
    return this.http.put<ReactionSummary>(`/api/posts/${encodeURIComponent(postId)}/my-reaction`, {
      reaction,
    });
  }

  removeReaction(postId: string): Observable<void> {
    return this.http.delete<void>(`/api/posts/${encodeURIComponent(postId)}/my-reaction`);
  }

  /** Authorized, cookie-protected URL of the post image (for <img src>). */
  contentUrl(postId: string): string {
    return `/api/posts/${encodeURIComponent(postId)}/content`;
  }

  downloadUrl(postId: string): string {
    return `/api/posts/${encodeURIComponent(postId)}/download`;
  }
}

function paging(page: number, pageSize: number): HttpParams {
  return new HttpParams().set('page', page).set('pageSize', pageSize);
}
