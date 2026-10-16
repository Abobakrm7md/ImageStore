export type NotificationType =
  | 'chatMessage'
  | 'newFollower'
  | 'postComment'
  | 'postReaction'
  | 'commentReply'
  | 'commentMention'
  | 'sharedPost'
  | 'systemAnnouncement';
export interface InboxNotification {
  id: string;
  type: NotificationType;
  text: string;
  actorDisplayName: string | null;
  createdAtUtc: string;
  readAtUtc: string | null;
  critical: boolean;
  available: boolean;
  actorUserId: string | null;
  postId: string | null;
  commentId: string | null;
  conversationId: string | null;
  messageId: string | null;
}
export interface NotificationPreferences {
  inAppEnabled: boolean;
  chatEnabled: boolean;
  followEnabled: boolean;
  commentsEnabled: boolean;
  reactionsEnabled: boolean;
  mentionsEnabled: boolean;
  announcementsEnabled: boolean;
  soundEnabled: boolean;
  soundVolume: number;
  suppressActiveConversation: boolean;
  browserPushEnabled: boolean;
}
export const defaultPreferences: NotificationPreferences = {
  inAppEnabled: true,
  chatEnabled: true,
  followEnabled: true,
  commentsEnabled: true,
  reactionsEnabled: true,
  mentionsEnabled: true,
  announcementsEnabled: true,
  soundEnabled: false,
  soundVolume: 35,
  suppressActiveConversation: true,
  browserPushEnabled: false,
};
export interface NotificationPage {
  items: InboxNotification[];
  page: number;
  totalPages: number;
  totalCount: number;
}
export function presentationEnabled(n: InboxNotification, p: NotificationPreferences): boolean {
  if (n.critical) return true;
  if (!p.inAppEnabled) return false;
  switch (n.type) {
    case 'chatMessage':
    case 'sharedPost':
      return p.chatEnabled;
    case 'newFollower':
      return p.followEnabled;
    case 'postComment':
    case 'commentReply':
      return p.commentsEnabled;
    case 'postReaction':
      return p.reactionsEnabled;
    case 'commentMention':
      return p.mentionsEnabled;
    case 'systemAnnouncement':
      return p.announcementsEnabled;
  }
}
export function relativeTime(value: string, now = Date.now()): string {
  const seconds = Math.max(0, Math.floor((now - Date.parse(value)) / 1000));
  if (seconds < 60) return 'Just now';
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} hr ago`;
  return `${Math.floor(seconds / 86400)} days ago`;
}
