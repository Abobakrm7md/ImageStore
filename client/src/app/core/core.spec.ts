import { HttpErrorResponse } from '@angular/common/http';
import { provideHttpClient, withInterceptors, HttpClient, HttpContext } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { authGuard, guestGuard } from './auth/auth.guards';
import { AuthService, SKIP_SESSION_REDIRECT, safeReturnUrl } from './auth/auth.service';
import { errorMessage, toApiError } from './http/api-error';
import { authInterceptor } from './http/auth.interceptor';
import { NotificationService } from './notifications/notification.service';

describe('toApiError', () => {
  it('reads Problem Details including field errors and codes', () => {
    const error = toApiError(
      new HttpErrorResponse({
        status: 400,
        error: {
          title: 'Invalid',
          status: 400,
          errors: { title: ['Title is required.'] },
          code: 'some_code',
        },
      }),
    );

    expect(error).toEqual({
      status: 400,
      title: 'Invalid',
      detail: undefined,
      fieldErrors: { title: ['Title is required.'] },
      code: 'some_code',
    });
  });

  it('describes network failures and bodies without Problem Details', () => {
    expect(toApiError(new HttpErrorResponse({ status: 0 })).title).toBe('Network error');
    expect(toApiError(new HttpErrorResponse({ status: 413, error: 'too big' })).title).toBe(
      'The upload is too large',
    );
    expect(toApiError(new Error('boom')).detail).toBe('boom');
  });

  it('prefers the first field error for messages', () => {
    const error = new HttpErrorResponse({
      status: 400,
      error: { title: 'Invalid', errors: { 'files[0]': ['Bad file.'] } },
    });
    expect(errorMessage(error)).toBe('Bad file.');
  });
});

describe('safeReturnUrl', () => {
  it.each([
    ['/images/123', '/images/123'],
    [undefined, '/feed'],
    ['https://evil.example', '/feed'],
    ['//evil.example', '/feed'],
    ['/\\evil.example', '/feed'],
  ])('maps %s to %s', (input, expected) => {
    expect(safeReturnUrl(input)).toBe(expected);
  });
});

describe('AuthService', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('fetches the anti-forgery token and restores the session on start-up', async () => {
    const initialized = auth.initialize();
    http.expectOne('/api/auth/csrf-token').flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    http.expectOne('/api/auth/me').flush({ id: 'u1', email: 'a@example.com' });
    await initialized;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.user()?.email).toBe('a@example.com');
  });

  it('treats a 401 from /me as signed out without redirecting', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate');
    const initialized = auth.initialize();
    http.expectOne('/api/auth/csrf-token').flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    http
      .expectOne('/api/auth/me')
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await initialized;

    expect(auth.isAuthenticated()).toBe(false);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('clears the local session on logout even if the request fails', async () => {
    const login = firstValueFrom(
      auth.login({ email: 'a@example.com', password: 'x', rememberMe: false }),
    );
    http.expectOne('/api/auth/login').flush({ id: 'u1', email: 'a@example.com' });
    await login;
    expect(auth.isAuthenticated()).toBe(true);

    const logout = firstValueFrom(auth.logout());
    http
      .expectOne('/api/auth/logout')
      .flush({ title: 'Bad' }, { status: 400, statusText: 'Bad Request' });
    await logout;

    expect(auth.isAuthenticated()).toBe(false);
  });
});

describe('route guards', () => {
  const state = (url: string) => ({ url }) as RouterStateSnapshot;
  const route = {} as ActivatedRouteSnapshot;

  function setup(signedIn: boolean) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const auth = TestBed.inject(AuthService);
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(signedIn);
    return TestBed.inject(Router);
  }

  it('sends anonymous users to the login page with a return URL', () => {
    const router = setup(false);
    const result = TestBed.runInInjectionContext(() => authGuard(route, state('/images/upload')));
    expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Fimages%2Fupload');
  });

  it('lets signed-in users through and keeps them away from the login page', () => {
    const router = setup(true);
    expect(TestBed.runInInjectionContext(() => authGuard(route, state('/images')))).toBe(true);
    const result = TestBed.runInInjectionContext(() => guestGuard(route, state('/login')));
    expect(router.serializeUrl(result as UrlTree)).toBe('/feed');
  });
});

describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('ends the session on 401 responses', async () => {
    const expired = vi.spyOn(auth, 'handleSessionExpired').mockImplementation(() => undefined);
    const request = firstValueFrom(client.get('/api/images')).catch((error: unknown) => error);
    http
      .expectOne('/api/images')
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(await request).toBeInstanceOf(HttpErrorResponse);
    expect(expired).toHaveBeenCalledTimes(1);
  });

  it('does not redirect for requests that opt out', async () => {
    const expired = vi.spyOn(auth, 'handleSessionExpired').mockImplementation(() => undefined);
    const context = new HttpContext().set(SKIP_SESSION_REDIRECT, true);
    const request = firstValueFrom(client.get('/api/auth/me', { context })).catch(
      (error: unknown) => error,
    );
    http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });

    await request;
    expect(expired).not.toHaveBeenCalled();
  });

  it('refreshes the anti-forgery token once and retries the request', async () => {
    const request = firstValueFrom(client.delete('/api/images/1'));
    http
      .expectOne('/api/images/1')
      .flush(
        { title: 'Invalid anti-forgery token', code: 'antiforgery_validation_failed' },
        { status: 400, statusText: 'Bad Request' },
      );
    http.expectOne('/api/auth/csrf-token').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/images/1').flush(null, { status: 204, statusText: 'No Content' });

    await expect(request).resolves.toBeNull();
  });

  it('does not retry twice when the token is still rejected', async () => {
    const problem = { title: 'Invalid anti-forgery token', code: 'antiforgery_validation_failed' };
    const request = firstValueFrom(client.delete('/api/images/1')).catch((error: unknown) => error);
    http.expectOne('/api/images/1').flush(problem, { status: 400, statusText: 'Bad Request' });
    http.expectOne('/api/auth/csrf-token').flush(null, { status: 204, statusText: 'No Content' });
    http.expectOne('/api/images/1').flush(problem, { status: 400, statusText: 'Bad Request' });

    expect(((await request) as HttpErrorResponse).status).toBe(400);
  });

  it('notifies the user about 403 responses', async () => {
    const notify = vi.spyOn(TestBed.inject(NotificationService), 'error');
    const request = firstValueFrom(client.get('/api/images')).catch((error: unknown) => error);
    http.expectOne('/api/images').flush({}, { status: 403, statusText: 'Forbidden' });

    await request;
    expect(notify).toHaveBeenCalledOnce();
  });
});
