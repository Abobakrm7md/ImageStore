import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
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
        <span class="emoji" aria-hidden="true">{{ mainEmoji() }}</span>
        <span class="count">{{ totalCount() }}</span>
      </button>

      <div class="reaction-picker">
        <button
          type="button"
          class="picker-toggle"
          [disabled]="pending()"
          [attr.aria-expanded]="pickerOpen()"
          aria-label="Choose a reaction"
          (click)="pickerOpen.set(!pickerOpen())"
        >
          <span aria-hidden="true">＋</span>
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
                {{ reaction.emoji }}
              </button>
            }
          </div>
        }
      </div>

      @if (countsByType().length > 0) {
        <span class="reaction-breakdown" aria-hidden="true">
          @for (entry of countsByType(); track entry.reaction) {
            <span class="breakdown-item" [title]="entry.label + ': ' + entry.count">
              {{ entry.emoji }} {{ entry.count }}
            </span>
          }
        </span>
      }
    </div>
  `,
  styles: `
    .reaction-bar {
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
    }

    .reaction-main,
    .picker-toggle {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      color: var(--color-text);
      border-radius: 999px;
      padding: 0.25rem 0.65rem;
      font: inherit;
      font-size: 0.9rem;
      cursor: pointer;
    }

    .reaction-main:hover:not(:disabled),
    .picker-toggle:hover:not(:disabled) {
      border-color: var(--color-border-strong);
    }

    .reaction-main.active {
      background: var(--color-primary-soft);
      border-color: var(--color-primary);
      color: var(--color-primary);
      font-weight: 600;
    }

    .reaction-main:disabled,
    .picker-toggle:disabled,
    .picker-item:disabled {
      opacity: 0.6;
      cursor: default;
    }

    .reaction-picker {
      position: relative;
    }

    .picker {
      position: absolute;
      bottom: calc(100% + 0.35rem);
      left: 0;
      display: flex;
      gap: 0.15rem;
      padding: 0.3rem;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: 999px;
      box-shadow: var(--shadow-md);
      z-index: 5;
    }

    .picker-item {
      border: none;
      background: transparent;
      font-size: 1.25rem;
      line-height: 1;
      padding: 0.3rem;
      border-radius: 50%;
      cursor: pointer;
    }

    .picker-item:hover:not(:disabled) {
      background: var(--color-surface-muted);
      transform: scale(1.15);
    }

    .picker-item.selected {
      outline: 2px solid var(--color-primary);
    }

    .reaction-breakdown {
      display: inline-flex;
      gap: 0.5rem;
      color: var(--color-text-muted);
      font-size: 0.85rem;
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

  protected readonly mainEmoji = computed(
    () => reactionMeta(this.myReaction() ?? 'like').emoji,
  );

  protected readonly mainLabel = computed(() =>
    this.myReaction()
      ? `Remove your ${reactionMeta(this.myReaction()!).label.toLowerCase()}`
      : 'Like this post',
  );

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
          entry.reaction === current ? { ...entry, count: Math.max(0, entry.count - 1) } : { ...entry },
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
