import { TestBed } from '@angular/core/testing';
import { ImageLightbox } from './image-lightbox';

describe('Image viewer', () => {
  beforeEach(() => {
    HTMLDialogElement.prototype.showModal = function () {
      this.open = true;
    };
    HTMLDialogElement.prototype.close = function () {
      this.open = false;
      this.dispatchEvent(new Event('close'));
    };
  });
  function create() {
    const fixture = TestBed.createComponent(ImageLightbox);
    fixture.componentRef.setInput('src', '/test.jpg');
    fixture.componentRef.setInput('alt', 'A quiet morning');
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    return fixture;
  }
  it('locks body scroll only while open and emits close', () => {
    const fixture = create();
    const closed = vi.fn();
    fixture.componentInstance.closed.subscribe(closed);
    expect(document.body.style.overflow).toBe('hidden');
    fixture.nativeElement.querySelector('dialog').close();
    expect(closed).toHaveBeenCalledOnce();
    expect(document.body.style.overflow).toBe('');
  });
  it('restores an existing overflow value on destruction', () => {
    document.body.style.overflow = 'auto';
    const fixture = create();
    fixture.destroy();
    expect(document.body.style.overflow).toBe('auto');
    document.body.style.overflow = '';
  });
  it('navigates only to available collection neighbours', () => {
    const fixture = create();
    const next = vi.fn();
    const previous = vi.fn();
    fixture.componentInstance.next.subscribe(next);
    fixture.componentInstance.previous.subscribe(previous);
    const dialog = fixture.nativeElement.querySelector('dialog');
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowLeft' }));
    expect(previous).not.toHaveBeenCalled();
    fixture.componentRef.setInput('hasNext', true);
    fixture.detectChanges();
    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    expect(next).toHaveBeenCalledOnce();
  });
  it('shows an unavailable state and retries without changing the storage URL', () => {
    const fixture = create();
    fixture.nativeElement.querySelector('img').dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('This image is unavailable');
    fixture.nativeElement.querySelector('.viewer-state button').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('img').getAttribute('src')).toBe('/test.jpg');
  });
  it('resets zoom and failure state for the next image', () => {
    const fixture = create();
    fixture.nativeElement.querySelector('img').dispatchEvent(new Event('load'));
    fixture.detectChanges();
    fixture.nativeElement
      .querySelector('dialog')
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'z' }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.canvas.zoomed')).not.toBeNull();
    fixture.componentRef.setInput('src', '/next.jpg');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.canvas.zoomed')).toBeNull();
    expect(fixture.nativeElement.querySelector('.viewer-loading')).not.toBeNull();
  });
});
