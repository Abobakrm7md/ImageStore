import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Icon } from '../icon/icon';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../../core/http/api-error';
import { NotificationService } from '../../../core/notifications/notification.service';
import { PostApiService } from '../../../core/posts/post-api.service';
import { ReactionSummary, ReactionType } from '../../../core/models/api.models';
import { REACTIONS, reactionMeta } from '../../utils/reactions';

/**
 * Reaction controls for one post: a quick button for the current (or most common) reaction and
 * a picker with all six. Clicking the active reaction removes it; choosing another changes it.
 * Calls the API itself and emits the fresh summary so parents update counts without a reload.
 */
@Component({
  selector: 'app-reaction-bar',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="reaction-bar">
      <button
        type="button"
        class="reaction-main"
        [class.active]="myReaction() !== null"
        [disabled]="pending()"
        [attr.aria-label]="mainLabel()"
        [attr.aria-pressed]="myReaction() !== null"
        (click)="toggleMain()"
      >
        <app-icon [name]="mainIcon()" [size]="17" />
        <span class="count">{{ totalCount() }}</span>
      </button>

      <div
        class="reaction-picker"
        (keydown.escape)="pickerOpen.set(false)"
        (focusout)="onFocusOut($event)"
      >
        <button
          type="button"
          class="picker-toggle"
          [disabled]="pending()"
          [attr.aria-expanded]="pickerOpen()"
          aria-label="Choose a reaction"
          (click)="pickerOpen.set(!pickerOpen())"
        >
          <app-icon name="chevron-down" [size]="13" />
        </button>
        @if (pickerOpen()) {
          <div class="picker" role="group" aria-label="Reactions">
            @for (reaction of reactions; track reaction.type) {
              <button
                type="button"
                class="picker-item"
                [class.selected]="myReaction() === reaction.type"
                [disabled]="pending()"
                [title]="reaction.label"
                [attr.aria-label]="reaction.label"
                (click)="choose(reaction.type)"
              >
                <app-icon [name]="reaction.icon" [size]="20" /><span>{{ reaction.label }}</span>
              </button>
            }
          </div>
        }
      </div>

      @if (countsByType().length > 0) {
        <span class="reaction-breakdown" aria-hidden="true">
          @for (entry of countsByType(); track entry.reaction) {
            <span class="breakdown-item" [title]="entry.label + ': ' + entry.count">
              <app-icon [name]="entry.icon" [size]="13" /> {{ entry.count }}
            </span>
          }
        </span>
      }
    </div>
  `,
  styles: `
    :host {
      display: inline-block;
      min-width: 0;
    }
    .reaction-bar {
      display: flex;
      align-items: center;
      gap: 0.2rem;
      flex-wrap: wrap;
    }
    .reaction-main,
    .picker-toggle {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      gap: 0.4rem;
      min-height: 40px;
      border: 0;
      background: transparent;
      color: var(--color-text-muted);
      border-radius: var(--radius-sm);
      padding: 0.4rem 0.55rem;
      font: inherit;
      font-size: 0.8rem;
      cursor: pointer;
    }
    .picker-toggle {
      min-width: 32px;
    }
    .reaction-main:hover,
    .picker-toggle:hover {
      background: var(--color-surface-muted);
      color: var(--color-text);
    }
    .reaction-main.active {
      color: var(--color-primary);
      background: var(--color-primary-soft);
    }
    button:disabled {
      opacity: 0.5;
      cursor: wait;
    }
    .reaction-picker {
      position: relative;
    }
    .picker {
      position: absolute;
      z-index: 10;
      bottom: calc(100% + 0.5rem);
      left: -3rem;
      width: 210px;
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 0.25rem;
      padding: 0.5rem;
      border-radius: var(--radius-md);
      border: 1px solid var(--color-border-strong);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
    }
    .picker-item {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 0.3rem;
      min-height: 58px;
      border: 0;
      padding: 0.5rem 0.2rem;
      background: transparent;
      color: var(--color-text);
      border-radius: var(--radius-sm);
      cursor: pointer;
      font-size: 0.68rem;
    }
    .picker-item:hover,
    .picker-item.selected {
      color: var(--color-primary);
      background: var(--color-primary-soft);
    }
    .reaction-breakdown {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
      color: var(--color-text-muted);
      font-size: 0.7rem;
    }
    .breakdown-item {
      display: flex;
      align-items: center;
      gap: 0.2rem;
    }
  `,
})
export class ReactionBar {
  readonly postId = input.required<string>();
  readonly totalCount = input.required<number>();
  readonly myReaction = input<ReactionType | null>(null);
  /** Counts per type as delivered by the API (zeros included). */
  readonly counts = input.required<readonly { reaction: ReactionType; count: number }[]>();

  /** Emits the server summary after a successful change so parents can refresh. */
  readonly summaryChanged = output<ReactionSummary>();

  protected readonly reactions = REACTIONS;
  protected readonly pickerOpen = signal(false);
  protected readonly pending = signal(false);

  private readonly api = inject(PostApiService);
  private readonly notifications = inject(NotificationService);

  protected readonly countsByType = computed(() =>
    this.counts()
      .filter((entry) => entry.count > 0)
      .map((entry) => ({ ...entry, ...reactionMeta(entry.reaction) })),
  );

  protected readonly mainIcon = computed(() => reactionMeta(this.myReaction() ?? 'like').icon);

  protected readonly mainLabel = computed(() =>
    this.myReaction()
      ? `Remove your ${reactionMeta(this.myReaction()!).label.toLowerCase()}`
      : 'Like this post',
  );

  protected onFocusOut(event: FocusEvent): void {
    if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null))
      this.pickerOpen.set(false);
  }

  protected async toggleMain(): Promise<void> {
    const current = this.myReaction();
    if (this.pending()) {
      return;
    }

    if (current === null) {
      await this.choose('like');
      return;
    }

    this.pending.set(true);
    try {
      await firstValueFrom(this.api.removeReaction(this.postId()));
      // The DELETE answers 204, so derive the new summary locally (my reaction is gone).
      this.summaryChanged.emit({
        totalCount: Math.max(0, this.totalCount() - 1),
        counts: this.counts().map((entry) =>
          entry.reaction === current
            ? { ...entry, count: Math.max(0, entry.count - 1) }
            : { ...entry },
        ),
        myReaction: null,
      });
    } catch (error) {
      this.notifications.error(errorMessage(error));
    } finally {
      this.pending.set(false);
    }
  }

  protected async choose(reaction: ReactionType): Promise<void> {
    if (this.pending()) {
      return;
    }

    this.pending.set(true);
    try {
      const summary = await firstValueFrom(this.api.setReaction(this.postId(), reaction));
      this.summaryChanged.emit(summary);
      this.pickerOpen.set(false);
    } catch (error) {
      this.notifications.error(errorMessage(error));
    } finally {
      this.pending.set(false);
    }
  }
}
