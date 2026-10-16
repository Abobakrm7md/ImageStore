import { IconName } from '../components/icon/icon';
import { ReactionType } from '../../core/models/api.models';

export interface ReactionMeta {
  type: ReactionType;
  emoji: string;
  icon: IconName;
  label: string;
}

/** The six supported reactions, in the same order the API reports them. */
export const REACTIONS: readonly ReactionMeta[] = [
  { type: 'like', icon: 'thumb-up', emoji: '👍', label: 'Like' },
  { type: 'love', icon: 'heart', emoji: '❤️', label: 'Love' },
  { type: 'laugh', icon: 'smile', emoji: '😂', label: 'Laugh' },
  { type: 'wow', icon: 'surprised', emoji: '😮', label: 'Wow' },
  { type: 'sad', icon: 'sad', emoji: '😢', label: 'Sad' },
  { type: 'dislike', icon: 'thumb-down', emoji: '👎', label: 'Dislike' },
];

const BY_TYPE = new Map<ReactionType, ReactionMeta>(REACTIONS.map((meta) => [meta.type, meta]));

export function reactionMeta(type: ReactionType): ReactionMeta {
  return BY_TYPE.get(type) ?? REACTIONS[0];
}

/** Initials (up to two) for the avatar fallback, derived from the display name. */
export function initials(displayName: string): string {
  const parts = displayName
    .split(/\s+/)
    .map((part) => part.replace(/[^\p{L}\p{N}]/gu, ''))
    .filter((part) => part.length > 0);
  if (parts.length === 0) {
    return '?';
  }

  const first = [...parts[0]][0]!;
  const last = parts.length > 1 ? [...parts[parts.length - 1]!][0]! : '';
  return (first + last).toUpperCase();
}
