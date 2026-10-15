import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injectable,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmText?: string;
  danger?: boolean;
}

/** Promise-based confirmation prompts, rendered by the single <app-confirm-dialog> in the shell. */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private resolver?: (confirmed: boolean) => void;
  readonly request = signal<ConfirmOptions | null>(null);

  confirm(options: ConfirmOptions): Promise<boolean> {
    this.resolver?.(false); // A new prompt cancels any pending one.
    this.request.set(options);
    return new Promise<boolean>((resolve) => (this.resolver = resolve));
  }

  resolve(confirmed: boolean): void {
    const resolver = this.resolver;
    this.resolver = undefined;
    this.request.set(null);
    resolver?.(confirmed);
  }
}

@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog #dialog class="dialog" aria-labelledby="confirm-title" (cancel)="onCancel($event)">
      @if (confirm.request(); as request) {
        <h2 id="confirm-title">{{ request.title }}</h2>
        <p>{{ request.message }}</p>
        <div class="dialog-actions">
          <button type="button" class="btn btn-secondary" (click)="confirm.resolve(false)">
            Cancel
          </button>
          <button
            type="button"
            class="btn"
            [class.btn-danger]="request.danger"
            [class.btn-primary]="!request.danger"
            (click)="confirm.resolve(true)"
          >
            {{ request.confirmText ?? 'Confirm' }}
          </button>
        </div>
      }
    </dialog>
  `,
})
export class ConfirmDialog {
  protected readonly confirm = inject(ConfirmService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const element = this.dialog().nativeElement;
      if (this.confirm.request() && !element.open) {
        element.showModal();
      } else if (!this.confirm.request() && element.open) {
        element.close();
      }
    });
  }

  protected onCancel(event: Event): void {
    event.preventDefault(); // Escape key: resolve as "cancel" through the service.
    this.confirm.resolve(false);
  }
}
