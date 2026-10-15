import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Icon } from '../icon/icon';

/** Full-screen view of an image (native modal dialog: focus trapping and Escape to close). */
@Component({
  selector: 'app-image-lightbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <dialog
      #dialog
      class="lightbox"
      [attr.aria-label]="alt()"
      (close)="closed.emit()"
      (click)="onBackdropClick($event)"
    >
      @if (open()) {
        <img [src]="src()" [alt]="alt()" />
        <button type="button" class="lightbox-close" aria-label="Close" (click)="dialog.close()">
          <app-icon name="close" [size]="22" />
        </button>
      }
    </dialog>
  `,
  styles: `
    .lightbox {
      border: none;
      padding: 0;
      background: transparent;
      max-width: 96vw;
      max-height: 96vh;
      overflow: visible;
    }
    .lightbox::backdrop {
      background: rgb(10 12 16 / 88%);
    }
    img {
      display: block;
      max-width: 96vw;
      max-height: 92vh;
      object-fit: contain;
      border-radius: var(--radius-md);
    }
    .lightbox-close {
      position: fixed;
      top: 1rem;
      right: 1rem;
      width: 2.75rem;
      height: 2.75rem;
      border-radius: 50%;
      border: none;
      background: rgb(255 255 255 / 15%);
      color: #fff;
      display: grid;
      place-items: center;
      cursor: pointer;
    }
    .lightbox-close:hover,
    .lightbox-close:focus-visible {
      background: rgb(255 255 255 / 30%);
    }
  `,
})
export class ImageLightbox {
  readonly src = input.required<string>();
  readonly alt = input.required<string>();
  readonly open = input(false);
  readonly closed = output<void>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  constructor() {
    effect(() => {
      const element = this.dialog().nativeElement;
      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }

  protected onBackdropClick(event: MouseEvent): void {
    // Clicks on the backdrop target the dialog element itself.
    if (event.target === this.dialog().nativeElement) {
      this.dialog().nativeElement.close();
    }
  }
}
