import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Icon } from '../icon/icon';

/** Native modal: focus trapping/Escape, original uncropped image, optional collection navigation. */
@Component({
  selector: 'app-image-lightbox',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <dialog
      #dialog
      class="lightbox"
      [attr.aria-label]="alt()"
      (close)="onClosed()"
      (keydown)="onKey($event)"
    >
      @if (open()) {
        <header class="viewer-bar">
          <div class="viewer-title">
            <span class="eyebrow">ImageStore / Image viewer</span><strong>{{ alt() }}</strong>
          </div>
          @if (position()) {
            <span class="viewer-position">{{ position() }}</span>
          }
          <button
            class="btn btn-secondary"
            [disabled]="broken() || loading()"
            (click)="zoomed.set(!zoomed())"
            [attr.aria-pressed]="zoomed()"
          >
            <app-icon name="expand" [size]="17" />{{ zoomed() ? 'Fit to screen' : 'Actual size' }}
          </button>
          <button class="icon-button" aria-label="Close image viewer" (click)="dialog.close()">
            <app-icon name="close" [size]="22" />
          </button>
        </header>
        <div
          class="canvas"
          [class.zoomed]="zoomed()"
          (click)="onBackdropClick($event)"
          tabindex="0"
          aria-label="Image viewport; use arrow keys to browse the collection"
        >
          @if (broken()) {
            <div class="viewer-state" role="status">
              <app-icon name="image" [size]="36" />
              <p>This image is unavailable.</p>
              <button class="btn btn-secondary" (click)="broken.set(false); loading.set(true)">
                Try again
              </button>
            </div>
          } @else {
            @if (loading()) {
              <span class="viewer-loading" role="status"
                ><span class="spinner"></span> Loading image</span
              >
            }
            <img
              [src]="src()"
              [alt]="alt()"
              (load)="loading.set(false)"
              (error)="broken.set(true); loading.set(false)"
            />
          }
        </div>
        <footer class="viewer-footer">
          <span>Original image <span class="desktop-hint">· Esc to close · Z to zoom</span></span>
          @if (position()) {
            <div class="viewer-navigation">
              <button
                class="icon-button"
                [disabled]="!hasPrevious()"
                aria-label="Previous image"
                (click)="previous.emit()"
              >
                <app-icon name="chevron-left" /></button
              ><button
                class="icon-button"
                [disabled]="!hasNext()"
                aria-label="Next image"
                (click)="next.emit()"
              >
                <app-icon name="chevron-right" />
              </button>
            </div>
          }
        </footer>
      }
    </dialog>
  `,
  styles: `
    .lightbox {
      position: fixed;
      inset: 0;
      width: 100vw;
      height: 100dvh;
      max-width: none;
      max-height: none;
      margin: 0;
      padding: 0;
      border: 0;
      background: #0b0c0f;
      color: var(--color-text);
      overflow: hidden;
    }
    .lightbox[open] {
      display: grid;
      grid-template-rows: auto minmax(0, 1fr) auto;
    }
    .lightbox::backdrop {
      background: #08090c;
    }
    .viewer-bar {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 1rem 1.5rem;
      border-bottom: 1px solid var(--color-border);
      background: #141519;
    }
    .viewer-title {
      min-width: 0;
      flex: 1;
    }
    .viewer-title strong {
      display: block;
      font-size: 0.9rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .eyebrow {
      margin-bottom: 0.25rem;
      font-size: 0.6rem;
    }
    .viewer-position {
      color: var(--color-text-muted);
      font-size: 0.8rem;
      white-space: nowrap;
    }
    .canvas {
      min-height: 0;
      display: grid;
      place-items: center;
      overflow: auto;
      padding: 1rem;
      position: relative;
    }
    .canvas img {
      display: block;
      width: 100%;
      height: 100%;
      min-height: 0;
      object-fit: contain;
    }
    .canvas.zoomed {
      display: block;
    }
    .canvas.zoomed img {
      width: auto;
      height: auto;
      max-width: none;
      max-height: none;
      margin: auto;
    }
    .viewer-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      min-height: 64px;
      padding: 0.5rem 1.5rem;
      gap: 1rem;
      border-top: 1px solid var(--color-border);
      font-size: 0.75rem;
      color: var(--color-text-muted);
    }
    .viewer-navigation {
      display: flex;
      gap: 0.5rem;
    }
    .icon-button {
      color: var(--color-text);
    }
    .icon-button:disabled {
      opacity: 0.3;
    }
    .viewer-loading {
      position: absolute;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .viewer-state {
      text-align: center;
      color: var(--color-text-muted);
    }
    @media (max-width: 600px) {
      .viewer-bar {
        padding: 0.75rem;
        gap: 0.5rem;
        flex-wrap: wrap;
      }
      .viewer-title {
        flex-basis: calc(100% - 60px);
      }
      .viewer-bar > .icon-button {
        position: absolute;
        right: 0.75rem;
        top: 0.75rem;
      }
      .viewer-bar .btn {
        min-height: 36px;
        font-size: 0.75rem;
      }
      .canvas {
        padding: 0.25rem;
      }
      .viewer-footer {
        padding: 0.5rem 0.75rem;
      }
      .desktop-hint {
        display: none;
      }
    }
  `,
})
export class ImageLightbox {
  readonly src = input.required<string>();
  readonly alt = input.required<string>();
  readonly open = input(false);
  readonly position = input('');
  readonly hasPrevious = input(false);
  readonly hasNext = input(false);
  readonly closed = output<void>();
  readonly previous = output<void>();
  readonly next = output<void>();
  protected readonly zoomed = signal(false);
  protected readonly broken = signal(false);
  protected readonly loading = signal(true);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private previousOverflow: string | undefined;
  constructor() {
    effect(() => {
      this.src();
      this.zoomed.set(false);
      this.broken.set(false);
      this.loading.set(true);
    });
    effect(() => {
      const element = this.dialog().nativeElement;
      if (this.open() && !element.open) {
        this.previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
        this.restoreScroll();
      }
    });
    inject(DestroyRef).onDestroy(() => this.restoreScroll());
  }
  protected onClosed() {
    this.restoreScroll();
    this.closed.emit();
  }
  private restoreScroll() {
    if (this.previousOverflow !== undefined) {
      document.body.style.overflow = this.previousOverflow;
      this.previousOverflow = undefined;
    }
  }
  protected onBackdropClick(event: MouseEvent) {
    if (event.target === event.currentTarget) this.dialog().nativeElement.close();
  }
  protected onKey(event: KeyboardEvent) {
    if (event.key === 'ArrowLeft' && this.hasPrevious()) {
      event.preventDefault();
      this.previous.emit();
    }
    if (event.key === 'ArrowRight' && this.hasNext()) {
      event.preventDefault();
      this.next.emit();
    }
    if (event.key.toLowerCase() === 'z' && !this.broken() && !this.loading()) {
      event.preventDefault();
      this.zoomed.update((value) => !value);
    }
  }
}
