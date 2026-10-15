import {
  HttpContextToken,
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
  HttpResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, filter, switchMap, take, throwError } from 'rxjs';
import { AuthService, SKIP_SESSION_REDIRECT } from '../auth/auth.service';
import { NotificationService } from '../notifications/notification.service';
import { ProblemDetails } from '../models/api.models';

const ANTIFORGERY_RETRIED = new HttpContextToken<boolean>(() => false);
const ANTIFORGERY_FAILURE_CODE = 'antiforgery_validation_failed';

/**
 * Cross-cutting HTTP error handling:
 * - 401: the session is gone → clear local state and go to the login page.
 * - 400 with an anti-forgery failure: refresh the XSRF token once and retry the request.
 * - 403: tell the user they are not allowed to do that.
 *
 * The XSRF header itself is added by Angular's built-in XSRF interceptor, which runs after this
 * one, so a retried request automatically picks up the refreshed token.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const notifications = inject(NotificationService);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const problem = error.error as ProblemDetails | null;
      if (
        error.status === 400 &&
        problem?.code === ANTIFORGERY_FAILURE_CODE &&
        !request.context.get(ANTIFORGERY_RETRIED)
      ) {
        return next(new HttpRequest<null>('GET', '/api/auth/csrf-token')).pipe(
          filter((event) => event instanceof HttpResponse),
          take(1),
          switchMap(() =>
            next(request.clone({ context: request.context.set(ANTIFORGERY_RETRIED, true) })),
          ),
        );
      }

      if (error.status === 401 && !request.context.get(SKIP_SESSION_REDIRECT)) {
        auth.handleSessionExpired(router.url);
      } else if (error.status === 403) {
        notifications.error('You are not allowed to perform this action.');
      }

      return throwError(() => error);
    }),
  );
};
