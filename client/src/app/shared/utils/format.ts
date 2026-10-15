const UNITS = ['bytes', 'KB', 'MB', 'GB'];

/** Human-readable file size using binary multiples (1 KB = 1024 bytes). */
export function formatFileSize(bytes: number | null | undefined): string {
  if (bytes === null || bytes === undefined || !Number.isFinite(bytes) || bytes < 0) {
    return '—';
  }

  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024;
    unit++;
  }

  return unit === 0
    ? `${value} ${value === 1 ? 'byte' : 'bytes'}`
    : `${value.toFixed(value < 10 ? 1 : 0)} ${UNITS[unit]}`;
}
