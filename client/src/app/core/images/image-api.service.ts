import { HttpClient, HttpEvent, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, shareReplay, throwError } from 'rxjs';
import {
  ImageDto,
  ImageListQuery,
  PagedResult,
  UpdateImageRequest,
  UploadImagesResponse,
  UploadMetadata,
  UploadPolicy,
} from '../models/api.models';

/** Typed client for the image endpoints. All calls are same-origin and cookie-authenticated. */
@Injectable({ providedIn: 'root' })
export class ImageApiService {
  private readonly http = inject(HttpClient);
  private policy$?: Observable<UploadPolicy>;

  list(query: ImageListQuery): Observable<PagedResult<ImageDto>> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize)
      .set('sortBy', query.sortBy)
      .set('sortDirection', query.sortDirection);
    if (query.search) {
      params = params.set('search', query.search);
    }
    if (query.tag) {
      params = params.set('tag', query.tag);
    }

    return this.http.get<PagedResult<ImageDto>>('/api/images', { params });
  }

  get(id: string): Observable<ImageDto> {
    return this.http.get<ImageDto>(`/api/images/${encodeURIComponent(id)}`);
  }

  update(id: string, request: UpdateImageRequest): Observable<ImageDto> {
    return this.http.put<ImageDto>(`/api/images/${encodeURIComponent(id)}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/images/${encodeURIComponent(id)}`);
  }

  /** Uploads one file, emitting progress events (requires the XHR backend). */
  upload(file: File, metadata: UploadMetadata): Observable<HttpEvent<UploadImagesResponse>> {
    const form = new FormData();
    form.append('files', file, file.name);
    if (metadata.title) {
      form.append('title', metadata.title);
    }
    if (metadata.description) {
      form.append('description', metadata.description);
    }
    for (const tag of metadata.tags ?? []) {
      form.append('tags', tag);
    }

    return this.http.post<UploadImagesResponse>('/api/images', form, {
      reportProgress: true,
      observe: 'events',
    });
  }

  /** Server-side upload limits; cached for the session (a failed request is retried next time). */
  uploadPolicy(): Observable<UploadPolicy> {
    this.policy$ ??= this.http.get<UploadPolicy>('/api/images/upload-policy').pipe(
      catchError((error: unknown) => {
        this.policy$ = undefined;
        return throwError(() => error);
      }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    return this.policy$;
  }

  /** Authorized, cookie-protected URL of the image content (for <img src>). */
  contentUrl(id: string): string {
    return `/api/images/${encodeURIComponent(id)}/content`;
  }

  /** Authorized download URL (served as an attachment with the original file name). */
  downloadUrl(id: string): string {
    return `/api/images/${encodeURIComponent(id)}/download`;
  }
}
