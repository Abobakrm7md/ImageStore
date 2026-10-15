import { HttpEventType, HttpResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of } from 'rxjs';
import { ImageApiService } from '../core/images/image-api.service';
import { UploadImagesResponse, UploadPolicy } from '../core/models/api.models';
import { Pagination, buildPageItems } from '../shared/components/pagination/pagination';
import { SORT_OPTIONS, queryFromParams, sortValue } from './gallery/gallery-query';
import { UploadPage } from './upload/upload.page';

describe('gallery query', () => {
  it('uses sensible defaults', () => {
    expect(queryFromParams(convertToParamMap({}))).toEqual({
      page: 1,
      pageSize: 12,
      search: undefined,
      tag: undefined,
      sortBy: 'createdAt',
      sortDirection: 'desc',
    });
  });

  it('parses page, search, tag and sort from the URL and ignores garbage', () => {
    const query = queryFromParams(
      convertToParamMap({ page: '3', q: '  beach ', tag: 'travel', sort: 'title-desc' }),
    );
    expect(query).toMatchObject({
      page: 3,
      search: 'beach',
      tag: 'travel',
      sortBy: 'title',
      sortDirection: 'desc',
    });

    expect(queryFromParams(convertToParamMap({ page: '-4', sort: 'nonsense' }))).toMatchObject({
      page: 1,
      sortBy: 'createdAt',
    });
  });

  it('round-trips sort options', () => {
    for (const option of SORT_OPTIONS) {
      expect(sortValue(queryFromParams(convertToParamMap({ sort: option.value })))).toBe(
        option.value,
      );
    }
  });
});

describe('pagination', () => {
  it('builds windows with gaps', () => {
    expect(buildPageItems(1, 1)).toEqual([1]);
    expect(buildPageItems(1, 0)).toEqual([]);
    expect(buildPageItems(5, 10)).toEqual([1, 'gap', 4, 5, 6, 'gap', 10]);
    expect(buildPageItems(2, 4)).toEqual([1, 2, 3, 4]);
  });

  it('renders buttons and emits page changes', () => {
    const fixture = TestBed.createComponent(Pagination);
    fixture.componentRef.setInput('page', 2);
    fixture.componentRef.setInput('totalPages', 3);
    fixture.detectChanges();

    const emitted: number[] = [];
    fixture.componentInstance.pageChange.subscribe((page) => emitted.push(page));
    const element: HTMLElement = fixture.nativeElement;
    (element.querySelector('[aria-label="Page 3"]') as HTMLButtonElement).click();

    expect(element.querySelector('[aria-current="page"]')?.textContent?.trim()).toBe('2');
    expect(emitted).toEqual([3]);
  });
});

describe('UploadPage', () => {
  const policy: UploadPolicy = {
    maxFileSizeBytes: 1000,
    maxFilesPerRequest: 10,
    allowedExtensions: ['.jpg', '.jpeg', '.png', '.webp'],
    allowedContentTypes: ['image/jpeg', 'image/png', 'image/webp'],
    titleMaxLength: 200,
    descriptionMaxLength: 2000,
    maxTagsPerImage: 20,
    tagMaxLength: 50,
  };

  let uploads: Subject<unknown>[];
  let api: { uploadPolicy: () => unknown; upload: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    uploads = [];
    api = {
      uploadPolicy: () => of(policy),
      upload: vi.fn(() => {
        const events = new Subject<unknown>();
        uploads.push(events);
        return events;
      }),
    };
    globalThis.URL.createObjectURL = vi.fn(() => 'blob:preview');
    globalThis.URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: ImageApiService, useValue: api }],
    });
  });

  async function createPage() {
    const fixture = TestBed.createComponent(UploadPage);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture;
  }

  const file = (name: string, size: number, type: string) =>
    new File([new Uint8Array(size)], name, { type });
  const statuses = (page: UploadPage) =>
    (page as unknown as { items: () => { status: string }[] }).items().map((item) => item.status);

  it('pre-validates files on the client', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance as unknown as { addFiles(files: File[]): void };

    page.addFiles([
      file('ok.png', 10, 'image/png'),
      file('big.jpg', 5000, 'image/jpeg'),
      file('doc.pdf', 10, 'application/pdf'),
    ]);

    expect(statuses(fixture.componentInstance)).toEqual(['ready', 'invalid', 'invalid']);
  });

  it('uploads with limited concurrency, reports progress, and distinguishes rejected from failed', async () => {
    const fixture = await createPage();
    const page = fixture.componentInstance as unknown as {
      addFiles(files: File[]): void;
      uploadAll(): void;
    };
    page.addFiles([
      file('a.png', 10, 'image/png'),
      file('b.png', 10, 'image/png'),
      file('c.png', 10, 'image/png'),
    ]);

    page.uploadAll();
    expect(api.upload).toHaveBeenCalledTimes(2); // At most two uploads run in parallel.
    expect(statuses(fixture.componentInstance)).toEqual(['uploading', 'uploading', 'queued']);

    uploads[0].next({ type: HttpEventType.UploadProgress, loaded: 5, total: 10 });
    expect(
      (fixture.componentInstance as unknown as { items: () => { progress: number }[] }).items()[0]
        .progress,
    ).toBe(50);

    const body: UploadImagesResponse = { items: [] };
    uploads[0].next(new HttpResponse({ status: 201, body }));
    uploads[0].complete();
    expect(api.upload).toHaveBeenCalledTimes(3); // The queued file starts when a slot frees up.

    const { HttpErrorResponse } = await import('@angular/common/http');
    uploads[1].error(
      new HttpErrorResponse({ status: 400, error: { errors: { 'files[0]': ['Not an image.'] } } }),
    );
    uploads[2].error(new HttpErrorResponse({ status: 0 }));

    expect(statuses(fixture.componentInstance)).toEqual(['done', 'rejected', 'failed']);
  });
});
