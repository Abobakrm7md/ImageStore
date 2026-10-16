import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'feed' },
  {
    path: 'login',
    title: 'Sign in · ImageStore',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'register',
    title: 'Create account · ImageStore',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/register.page').then((m) => m.RegisterPage),
  },
  {
    path: 'feed',
    title: 'Feed · ImageStore',
    canActivate: [authGuard],
    loadComponent: () => import('./features/feed/feed.page').then((m) => m.FeedPage),
  },
  {
    path: 'posts',
    canActivate: [authGuard],
    children: [
      {
        // Must come before ':id' so the literal segment wins.
        path: 'new',
        title: 'New post · ImageStore',
        loadComponent: () => import('./features/post/new-post.page').then((m) => m.NewPostPage),
      },
      {
        path: ':id',
        title: 'Post · ImageStore',
        loadComponent: () =>
          import('./features/post/post-detail.page').then((m) => m.PostDetailPage),
      },
    ],
  },
  {
    path: 'users',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        title: 'People · ImageStore',
        loadComponent: () =>
          import('./features/users/user-search.page').then((m) => m.UserSearchPage),
      },
      {
        path: ':id',
        title: 'Profile · ImageStore',
        loadComponent: () =>
          import('./features/users/user-profile.page').then((m) => m.UserProfilePage),
      },
      {
        path: ':id/followers',
        title: 'Followers · ImageStore',
        loadComponent: () => import('./features/users/user-list.page').then((m) => m.UserListPage),
      },
      {
        path: ':id/following',
        title: 'Following · ImageStore',
        loadComponent: () => import('./features/users/user-list.page').then((m) => m.UserListPage),
      },
    ],
  },
  {
    path: 'notifications',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
  },
  {
    path: 'chat',
    canActivate: [authGuard],
    loadComponent: () => import('./features/chat/chat.page').then((m) => m.ChatPage),
  },
  {
    path: 'account',
    title: 'My account · ImageStore',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/account.page').then((m) => m.AccountPage),
  },
  {
    path: 'images',
    canActivate: [authGuard],
    children: [
      {
        path: '',
        title: 'My images · ImageStore',
        loadComponent: () => import('./features/gallery/gallery.page').then((m) => m.GalleryPage),
      },
      {
        path: 'upload',
        title: 'Upload · ImageStore',
        loadComponent: () => import('./features/upload/upload.page').then((m) => m.UploadPage),
      },
      {
        path: ':id',
        title: 'Image details · ImageStore',
        loadComponent: () =>
          import('./features/image-detail/image-detail.page').then((m) => m.ImageDetailPage),
      },
    ],
  },
  {
    path: '**',
    title: 'Not found · ImageStore',
    loadComponent: () => import('./features/not-found/not-found.page').then((m) => m.NotFoundPage),
  },
];
