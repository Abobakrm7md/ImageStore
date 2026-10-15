import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

/** Transient, non-blocking notifications ("toasts"). */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private nextId = 1;
  private readonly items = signal<Toast[]>([]);

  readonly toasts = this.items.asReadonly();

  success(message: string): void {
    this.show('success', message, 4000);
  }

  info(message: string): void {
    this.show('info', message, 5000);
  }

  error(message: string): void {
    this.show('error', message, 8000);
  }

  dismiss(id: number): void {
    this.items.update((toasts) => toasts.filter((toast) => toast.id !== id));
  }

  private show(kind: ToastKind, message: string, durationMs: number): void {
    const id = this.nextId++;
    // Keep the stack short so a burst of errors cannot flood the screen.
    this.items.update((toasts) => [...toasts.slice(-3), { id, kind, message }]);
    setTimeout(() => this.dismiss(id), durationMs);
  }
}
