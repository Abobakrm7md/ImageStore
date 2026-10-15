import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Icon, IconName } from '../icon/icon';

/** Friendly placeholder for empty lists, errors and missing resources. */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    <div class="empty-state" [class.empty-state-error]="tone() === 'error'">
      <div class="empty-icon"><app-icon [name]="icon()" [size]="40" /></div>
      <h2>{{ heading() }}</h2>
      @if (message()) {
        <p>{{ message() }}</p>
      }
      <div class="empty-actions"><ng-content /></div>
    </div>
  `,
})
export class EmptyState {
  readonly heading = input.required<string>();
  readonly message = input<string>();
  readonly icon = input<IconName>('image');
  readonly tone = input<'neutral' | 'error'>('neutral');
}
