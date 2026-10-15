import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  ImagePost,
  PagedResult,
  UpdateMyProfileRequest,
  UserProfile,
  UserSummary,
} from '../models/api.models';

export interface UsersQuery {
  search?: string;
  page: number;
  pageSize: number;
}

/** Typed client for profiles, search and following. Cookie-authenticated, same-origin. */
@Injectable({ providedIn: 'root' })
export class SocialApiService {
  private readonly http = inject(HttpClient);

  search(query: UsersQuery): Observable<PagedResult<UserSummary>> {
    let params = paging(query.page, query.pageSize);
    if (query.search) {
      params = params.set('search', query.search);
    }

    return this.http.get<PagedResult<UserSummary>>('/api/users', { params });
  }

  profile(userId: string): Observable<UserProfile> {
    return this.http.get<UserProfile>(`/api/users/${encodeURIComponent(userId)}`);
  }

  myProfile(): Observable<UserProfile> {
    return this.http.get<UserProfile>('/api/users/me');
  }

  updateMyProfile(request: UpdateMyProfileRequest): Observable<UserProfile> {
    return this.http.put<UserProfile>('/api/users/me', request);
  }

  follow(userId: string): Observable<void> {
    return this.http.post<void>(`/api/users/${encodeURIComponent(userId)}/follow`, null);
  }

  unfollow(userId: string): Observable<void> {
    return this.http.delete<void>(`/api/users/${encodeURIComponent(userId)}/follow`);
  }

  followers(userId: string, page: number, pageSize: number): Observable<PagedResult<UserSummary>> {
    return this.http.get<PagedResult<UserSummary>>(`/api/users/${encodeURIComponent(userId)}/followers`, {
      params: paging(page, pageSize),
    });
  }

  following(userId: string, page: number, pageSize: number): Observable<PagedResult<UserSummary>> {
    return this.http.get<PagedResult<UserSummary>>(`/api/users/${encodeURIComponent(userId)}/following`, {
      params: paging(page, pageSize),
    });
  }

  /** Published posts of a user; the API answers 404 unless the caller is the user or a follower. */
  userPosts(userId: string, page: number, pageSize: number): Observable<PagedResult<ImagePost>> {
    return this.http.get<PagedResult<ImagePost>>(`/api/users/${encodeURIComponent(userId)}/posts`, {
      params: paging(page, pageSize),
    });
  }

  /** Authorized, cookie-protected URL of the profile picture (for <img src>). */
  profilePictureUrl(userId: string): string {
    return `/api/users/${encodeURIComponent(userId)}/profile-picture`;
  }

  setProfilePicture(file: File): Observable<UserProfile> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.put<UserProfile>('/api/users/me/profile-picture', form);
  }

  removeProfilePicture(): Observable<void> {
    return this.http.delete<void>('/api/users/me/profile-picture');
  }
}

function paging(page: number, pageSize: number): HttpParams {
  return new HttpParams().set('page', page).set('pageSize', pageSize);
}
