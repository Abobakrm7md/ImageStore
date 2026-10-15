import { Pipe, PipeTransform } from '@angular/core';
import { formatFileSize } from '../utils/format';

@Pipe({ name: 'fileSize' })
export class FileSizePipe implements PipeTransform {
  transform(bytes: number | null | undefined): string {
    return formatFileSize(bytes);
  }
}
