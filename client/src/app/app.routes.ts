import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'images' },
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
