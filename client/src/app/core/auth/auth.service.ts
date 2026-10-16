import { HttpClient, HttpContext, HttpContextToken } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, firstValueFrom, map, of, tap } from 'rxjs';
import { CurrentUser, LoginRequest, RegisterRequest } from '../models/api.models';
import { NotificationService } from '../notifications/notification.service';

/** Marks requests whose 401 responses must not trigger the "session expired" redirect. */
export const SKIP_SESSION_REDIRECT = new HttpContextToken<boolean>(() => false);

const authContext = () => new HttpContext().set(SKIP_SESSION_REDIRECT, true);

/**
 * Client-side view of the cookie session. The session itself lives in an HttpOnly cookie that
 * JavaScript cannot read; this service only tracks who is signed in for rendering purposes.
 * The server enforces authentication and ownership on every request.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly currentUser = signal<CurrentUser | null>(null);

  readonly user = this.currentUser.asReadonly();
  readonly isAuthenticated = computed(() => this.currentUser() !== null);

  /**
   * Runs once at start-up: obtains the anti-forgery token cookie and restores an existing
   * session, so route guards know the authentication state before the first navigation.
   */
  async initialize(): Promise<void> {
    try {
      await firstValueFrom(this.http.get<void>('/api/auth/csrf-token', { context: authContext() }));
      const user = await firstValueFrom(
        this.http.get<CurrentUser>('/api/auth/me', { context: authContext() }),
      );
      this.currentUser.set(user);
    } catch {
      this.currentUser.set(null);
    }
  }

  login(request: LoginRequest): Observable<CurrentUser> {
    return this.http
      .post<CurrentUser>('/api/auth/login', request, { context: authContext() })
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  register(request: RegisterRequest): Observable<CurrentUser> {
    return this.http
      .post<CurrentUser>('/api/auth/register', request, { context: authContext() })
      .pipe(tap((user) => this.currentUser.set(user)));
  }

  /** Signs out. The local state is cleared even if the request fails (e.g. expired session). */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', null, { context: authContext() }).pipe(
      catchError(() => of(undefined)),
      map(() => undefined),
      tap(() => this.currentUser.set(null)),
    );
  }

  /** Called by the HTTP interceptor when the server rejects the session (401). */
  handleSessionExpired(returnUrl: string): void {
    const wasSignedIn = this.currentUser() !== null;
    this.currentUser.set(null);
    if (wasSignedIn) {
      this.notifications.info('Your session has ended. Please sign in again.');
    }

    const target = returnUrl.startsWith('/login') ? undefined : returnUrl;
    void this.router.navigate(['/login'], { queryParams: target ? { returnUrl: target } : {} });
  }
}

/** Only allow in-app return URLs, preventing open redirects through ?returnUrl=. */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/feed';
  }

  return value;
}
