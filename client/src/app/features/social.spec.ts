import { HttpClient, provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { throwError, of } from 'rxjs';
import { NotificationService } from '../core/notifications/notification.service';
import { PostApiService } from '../core/posts/post-api.service';
import { SocialApiService } from '../core/social/social-api.service';
import { ReactionSummary } from '../core/models/api.models';
import { FollowButton } from '../shared/components/follow-button/follow-button';
import { ReactionBar } from '../shared/components/reaction-bar/reaction-bar';
import { initials, reactionMeta } from '../shared/utils/reactions';

describe('SocialApiService', () => {
  let http: HttpClient;
  let controller: HttpTestingController;
  let api: SocialApiService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    controller = TestBed.inject(HttpTestingController);
    api = TestBed.inject(SocialApiService);
  });

  afterEach(() => controller.verify());

  it('sends search and paging as query parameters', () => {
    api.search({ search: 'ada', page: 2, pageSize: 20 }).subscribe();
    const request = controller.expectOne(
      (req) => req.url === '/api/users' && req.method === 'GET',
    );
    expect(request.request.params.get('search')).toBe('ada');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('20');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0, totalPages: 0 });
  });

  it('omits the search parameter for an empty query', () => {
    api.search({ page: 1, pageSize: 20 }).subscribe();
    const request = controller.expectOne((req) => req.url === '/api/users');
    expect(request.request.params.has('search')).toBe(false);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
  });

  it('follows with POST and unfollows with DELETE, returning 204 bodies', () => {
    api.follow('user-1').subscribe();
    controller.expectOne('/api/users/user-1/follow').flush(null, { status: 204, statusText: 'No Content' });

    api.unfollow('user-1').subscribe();
    const request = controller.expectOne('/api/users/user-1/follow');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('builds authorized URLs for profile pictures and post content', () => {
    expect(api.profilePictureUrl('u/1')).toBe('/api/users/u%2F1/profile-picture');
    const posts = TestBed.inject(PostApiService);
    expect(posts.contentUrl('p 1')).toBe('/api/posts/p%201/content');
    expect(posts.downloadUrl('p 1')).toBe('/api/posts/p%201/download');
  });
});

describe('PostApiService', () => {
  let controller: HttpTestingController;
  let api: PostApiService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    controller = TestBed.inject(HttpTestingController);
    api = TestBed.inject(PostApiService);
  });

  afterEach(() => controller.verify());

  it('pages the feed', () => {
    api.feed(3, 10).subscribe();
    const request = controller.expectOne((req) => req.url === '/api/feed');
    expect(request.request.params.get('page')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('10');
    request.flush({ items: [], page: 3, pageSize: 10, totalCount: 0, totalPages: 0 });
  });

  it('sets a reaction with PUT and the reaction name as body', () => {
    api.setReaction('post-1', 'love').subscribe();
    const request = controller.expectOne('/api/posts/post-1/my-reaction');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ reaction: 'love' });
    request.flush({ totalCount: 1, counts: [], myReaction: 'love' });
  });

  it('removes a reaction with DELETE', () => {
    api.removeReaction('post-1').subscribe();
    const request = controller.expectOne('/api/posts/post-1/my-reaction');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('creates and updates comments under the post', () => {
    api.createComment('post-1', 'nice!').subscribe();
    const created = controller.expectOne('/api/posts/post-1/comments');
    expect(created.request.body).toEqual({ content: 'nice!' });
    created.flush({});

    api.updateComment('post-1', 'c-2', 'edited').subscribe();
    const updated = controller.expectOne('/api/posts/post-1/comments/c-2');
    expect(updated.request.method).toBe('PUT');
    expect(updated.request.body).toEqual({ content: 'edited' });
    updated.flush({});
  });
});

describe('reaction utils', () => {
  it('derives up to two initials and falls back to ?', () => {
    expect(initials('Ada Lovelace')).toBe('AL');
    expect(initials('  grace   hopper  ')).toBe('GH');
    expect(initials('Plato')).toBe('P');
    expect(initials('***')).toBe('?');
    expect(initials('')).toBe('?');
    expect(initials('أحمد سعيد')).toBe('أس');
  });

  it('maps every reaction type to stable metadata', () => {
    expect(reactionMeta('love').emoji).toBe('❤️');
    expect(reactionMeta('dislike').label).toBe('Dislike');
  });
});

describe('FollowButton', () => {
  function setup(followImpl: () => unknown) {
    const api = { follow: vi.fn(followImpl), unfollow: vi.fn(followImpl) };
    const notifications = { error: vi.fn(), success: vi.fn(), info: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SocialApiService, useValue: api },
        { provide: NotificationService, useValue: notifications },
      ],
    });
    const fixture = TestBed.createComponent(FollowButton);
    fixture.componentRef.setInput('userId', 'user-1');
    return { fixture, api, notifications };
  }

  const button = (fixture: { nativeElement: HTMLElement }) =>
    fixture.nativeElement.querySelector('button') as HTMLButtonElement;

  it('follows, updates the model and emits changed', async () => {
    const { fixture, api } = setup(() => of(undefined));
    fixture.componentRef.setInput('following', false);
    fixture.detectChanges();
    expect(button(fixture).textContent).toContain('Follow');

    const emitted: boolean[] = [];
    fixture.componentInstance.changed.subscribe((value) => emitted.push(value));
    button(fixture).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.follow).toHaveBeenCalledWith('user-1');
    expect(emitted).toEqual([true]);
    expect(button(fixture).textContent).toContain('Following');
    expect(button(fixture).getAttribute('aria-pressed')).toBe('true');
  });

  it('unfollows when already following', async () => {
    const { fixture, api } = setup(() => of(undefined));
    fixture.componentRef.setInput('following', true);
    fixture.detectChanges();

    button(fixture).click();
    await fixture.whenStable();

    expect(api.unfollow).toHaveBeenCalledWith('user-1');
    expect(api.follow).not.toHaveBeenCalled();
    expect(fixture.componentInstance.following()).toBe(false);
  });

  it('keeps the state and reports an error when the request fails', async () => {
    const failure = throwError(
      () => new HttpErrorResponse({ status: 500, statusText: 'Server Error' }),
    );
    const { fixture, notifications } = setup(() => failure);
    fixture.componentRef.setInput('following', false);
    fixture.detectChanges();

    const emitted: boolean[] = [];
    fixture.componentInstance.changed.subscribe((value) => emitted.push(value));
    button(fixture).click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
    expect(notifications.error).toHaveBeenCalled();
    expect(fixture.componentInstance.following()).toBe(false);
    expect(button(fixture).disabled).toBe(false); // pending flag is reset
  });
});

describe('ReactionBar', () => {
  const counts: { reaction: 'like' | 'love' | 'laugh' | 'wow' | 'sad' | 'dislike'; count: number }[] = [
    { reaction: 'like', count: 2 },
    { reaction: 'love', count: 1 },
    { reaction: 'laugh', count: 0 },
    { reaction: 'wow', count: 0 },
    { reaction: 'sad', count: 0 },
    { reaction: 'dislike', count: 0 },
  ];

  function setup() {
    const summary: ReactionSummary = {
      totalCount: 4,
      counts: counts.map((entry) =>
        entry.reaction === 'love' ? { ...entry, count: 2 } : { ...entry },
      ),
      myReaction: 'love',
    };
    const api = {
      setReaction: vi.fn(() => of(summary)),
      removeReaction: vi.fn(() => of(undefined)),
    };
    const notifications = { error: vi.fn(), success: vi.fn(), info: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PostApiService, useValue: api },
        { provide: NotificationService, useValue: notifications },
      ],
    });
    const fixture = TestBed.createComponent(ReactionBar);
    fixture.componentRef.setInput('postId', 'post-1');
    fixture.componentRef.setInput('totalCount', 3);
    fixture.componentRef.setInput('counts', counts);
    return { fixture, api, notifications, summary };
  }

  const mainButton = (fixture: { nativeElement: HTMLElement }) =>
    fixture.nativeElement.querySelector('.reaction-main') as HTMLButtonElement;

  it('adds the default like when no reaction is active', async () => {
    const { fixture, api } = setup();
    fixture.componentRef.setInput('myReaction', null);
    fixture.detectChanges();
    expect(mainButton(fixture).getAttribute('aria-label')).toBe('Like this post');

    mainButton(fixture).click();
    await fixture.whenStable();

    expect(api.setReaction).toHaveBeenCalledWith('post-1', 'like');
  });

  it('chooses another reaction from the picker and emits the server summary', async () => {
    const { fixture, api, summary } = setup();
    fixture.componentRef.setInput('myReaction', null);
    fixture.detectChanges();

    const emitted: ReactionSummary[] = [];
    fixture.componentInstance.summaryChanged.subscribe((value) => emitted.push(value));

    (fixture.nativeElement.querySelector('.picker-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();
    const love = fixture.nativeElement.querySelector('[aria-label="Love"]') as HTMLButtonElement;
    expect(love).toBeTruthy();
    love.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(api.setReaction).toHaveBeenCalledWith('post-1', 'love');
    expect(emitted).toEqual([summary]);
    // The picker closes again after choosing.
    expect(fixture.nativeElement.querySelector('.picker-item')).toBeNull();
  });

  it('derives the summary locally when the active reaction is removed (204)', async () => {
    const { fixture, api } = setup();
    fixture.componentRef.setInput('myReaction', 'love');
    fixture.detectChanges();
    expect(mainButton(fixture).getAttribute('aria-label')).toBe('Remove your love');

    const emitted: ReactionSummary[] = [];
    fixture.componentInstance.summaryChanged.subscribe((value) => emitted.push(value));

    mainButton(fixture).click();
    await fixture.whenStable();

    expect(api.removeReaction).toHaveBeenCalledWith('post-1');
    expect(emitted).toEqual([
      {
        totalCount: 2,
        counts: counts.map((entry) =>
          entry.reaction === 'love' ? { ...entry, count: 0 } : { ...entry },
        ),
        myReaction: null,
      },
    ]);
  });

  it('shows a breakdown chip only for non-zero counts', () => {
    const { fixture } = setup();
    fixture.componentRef.setInput('myReaction', 'love');
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('.breakdown-item');
    expect(chips.length).toBe(2); // like: 2, love: 1
  });
});
