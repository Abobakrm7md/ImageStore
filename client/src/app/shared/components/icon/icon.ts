import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Minimal inline SVG icon set (24×24, stroke-based). Inline SVG renders identically everywhere,
 * unlike emoji or symbol glyphs, which depend on the fonts installed on the device.
 */
const ICONS = {
  'chevron-down': ['M6 9l6 6 6-6'],
  'chevron-left': ['M15 6l-6 6 6 6'],
  'chevron-right': ['M9 6l6 6-6 6'],
  settings: [
    'M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8z',
    'M12 2v3m0 14v3M2 12h3m14 0h3M5 5l2 2m10 10 2 2M5 19l2-2M17 7l2-2',
  ],
  logout: ['M9 4H4v16h5', 'M10 12h11m-4-4 4 4-4 4'],
  grid: ['M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM14 14h7v7h-7z'],
  'thumb-up': ['M7 10v11H3V10h4z', 'M7 10l5-8h2v7h5a2 2 0 0 1 2 2l-2 8a2 2 0 0 1-2 2H7'],
  'thumb-down': ['M7 14V3H3v11h4z', 'M7 14l5 8h2v-7h5a2 2 0 0 0 2-2l-2-8a2 2 0 0 0-2-2H7'],
  smile: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z', 'M8 14s1 3 4 3 4-3 4-3M8 9h.01M16 9h.01'],
  surprised: [
    'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z',
    'M8 9h.01M16 9h.01M12 13a2 2 0 1 1 0 4 2 2 0 0 1 0-4z',
  ],
  sad: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z', 'M8 16s1-3 4-3 4 3 4 3M8 9h.01M16 9h.01'],
  volume: ['M11 4 6 8H2v8h4l5 4V4z', 'M15 8a6 6 0 0 1 0 8M18 5a10 10 0 0 1 0 14'],
  mention: ['M16 8v6a2 2 0 0 0 4 0v-2a8 8 0 1 0-3 6', 'M16 12a4 4 0 1 1-8 0 4 4 0 0 1 8 0z'],
  reply: ['M9 5 3 11l6 6', 'M3 11h11a7 7 0 0 1 7 7'],
  lock: ['M6 10h12v11H6z', 'M8 10V6a4 4 0 0 1 8 0v4'],
  bell: ['M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9', 'M10 21h4'],
  alert: [
    'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z',
    'M12 9v4',
    'M12 17h.01',
  ],
  'arrow-left': ['M19 12H5', 'M12 19l-7-7 7-7'],
  check: ['M5 12l5 5L20 7'],
  close: ['M6 6l12 12', 'M18 6 6 18'],
  compass: ['M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0z', 'M15.5 8.5l-2 5-5 2 2-5z'],
  download: ['M12 4v11', 'M7 10l5 5 5-5', 'M4 20h16'],
  edit: ['M4 20h4L18.5 9.5a2.1 2.1 0 0 0-3-3L5 17v3z', 'M13.5 6.5l3 3'],
  expand: ['M15 3h6v6', 'M9 21H3v-6', 'M21 3l-7 7', 'M3 21l7-7'],
  heart: [
    'M20.8 5.6a5 5 0 0 0-7.1 0L12 7.3l-1.7-1.7a5 5 0 1 0-7.1 7.1l8.8 8.8 8.8-8.8a5 5 0 0 0 0-7.1z',
  ],
  message: ['M21 12a8 8 0 0 1-8 8H5l-2 2V12a8 8 0 0 1 8-8h2a8 8 0 0 1 8 8z'],
  user: ['M20 21v-2a5 5 0 0 0-5-5H9a5 5 0 0 0-5 5v2', 'M12 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8z'],
  users: [
    'M17 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2',
    'M9.5 3a4 4 0 1 1 0 8 4 4 0 0 1 0-8z',
    'M22 21v-2a4 4 0 0 0-3-3.87',
    'M16 3.13a4 4 0 0 1 0 7.75',
  ],
  send: ['M22 2 11 13', 'M22 2l-7 20-4-9-9-4z'],
  image: [
    'M5 4h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
    'M11 10a2 2 0 1 1-4 0 2 2 0 0 1 4 0z',
    'M21 16l-5-5-9 9',
  ],
  logo: [
    'M6 3h12a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6a3 3 0 0 1 3-3z',
    'M3 15l5-5 4 4 3-3 6 6',
  ],
  menu: ['M4 6h16', 'M4 12h16', 'M4 18h16'],
  plus: ['M12 5v14', 'M5 12h14'],
  search: ['M18 11a7 7 0 1 1-14 0 7 7 0 0 1 14 0z', 'M20 20l-4.3-4.3'],
  trash: [
    'M4 7h16',
    'M10 11v6',
    'M14 11v6',
    'M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12',
    'M9 7V4h6v3',
  ],
  upload: ['M12 15V4', 'M7 9l5-5 5 5', 'M4 15v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4'],
} as const satisfies Record<string, readonly string[]>;

export type IconName = keyof typeof ICONS;

@Component({
  selector: 'app-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'icon' },
  template: `
    <svg
      viewBox="0 0 24 24"
      [attr.width]="size()"
      [attr.height]="size()"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      @for (d of paths(); track $index) {
        <path [attr.d]="d" />
      }
    </svg>
  `,
  styles: `
    :host {
      display: inline-flex;
      flex-shrink: 0;
      line-height: 0;
    }
  `,
})
export class Icon {
  readonly name = input.required<IconName>();
  readonly size = input(20);

  protected readonly paths = computed(() => ICONS[this.name()]);
}
