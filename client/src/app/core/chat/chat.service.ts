import { Injectable, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { UserSummary } from '../models/api.models';

export interface Message {
  id: string;
  conversationId: string;
  senderUserId: string;
  sequence: number;
  content: string;
  createdAtUtc: string;
  messageType: string;
  sharedPostId: string | null;
}
export interface Conversation {
  id: string;
  participants: (UserSummary & { lastReadSequence: number })[];
  latestMessage: Message | null;
  unreadCount: number;
}
export interface ChatPage {
  items: Conversation[];
  page: number;
  totalPages: number;
}
export interface SendRequest {
  id: string;
  content: string;
  sharedPostId?: string;
}

@Injectable({ providedIn: 'root' })
export class ChatService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  readonly status = signal('Disconnected');
  readonly revision = signal(0);
  readonly typing = signal<{ id: string; until: number } | null>(null);
  private hub?: HubConnection;
  private active?: string;
  private lastTyping = 0;
  constructor() {
    effect((cleanup) => {
      if (!this.auth.user()) return;
      const token = decodeURIComponent(
        document.cookie.match(/(?:^|;\s*)XSRF-TOKEN=([^;]+)/)?.[1] ?? '',
      );
      const hub = new HubConnectionBuilder()
        .withUrl('/api/chat', { headers: { 'X-XSRF-TOKEN': token } })
        .configureLogging(LogLevel.None)
        .withAutomaticReconnect()
        .build();
      this.hub = hub;
      let closed = false;
      let retry: ReturnType<typeof setTimeout> | undefined;
      const recover = async () => {
        this.status.set('Connected');
        if (this.active) await hub.invoke('Join', this.active).catch(() => {});
        this.revision.update((n) => n + 1);
      };
      const start = async () => {
        if (closed) return;
        this.status.set('Connecting');
        try {
          await hub.start();
          if (!closed) await recover();
        } catch {
          if (!closed) {
            this.status.set('Disconnected — retrying');
            retry = setTimeout(() => void start(), 5000);
          }
        }
      };
      hub.on('ConversationChanged', () => this.revision.update((n) => n + 1));
      hub.on('ReadUpdated', () => this.revision.update((n) => n + 1));
      hub.on('Typing', (id: string, _user: string, typing: boolean) =>
        this.typing.set(typing ? { id, until: Date.now() + 4000 } : null),
      );
      hub.onreconnecting(() => this.status.set('Reconnecting'));
      hub.onreconnected(recover);
      hub.onclose(() => {
        if (!closed) {
          this.status.set('Disconnected — retrying');
          retry = setTimeout(() => void start(), 5000);
        }
      });
      void start();
      cleanup(() => {
        closed = true;
        clearTimeout(retry);
        this.hub = undefined;
        this.active = undefined;
        this.typing.set(null);
        this.status.set('Disconnected');
        void hub.stop();
      });
    });
  }
  list(page = 1) {
    return firstValueFrom(this.http.get<ChatPage>('/api/conversations', { params: { page } }));
  }
  open(otherUserId: string) {
    return firstValueFrom(this.http.post<Conversation>('/api/conversations', { otherUserId }));
  }
  get(id: string) {
    return firstValueFrom(this.http.get<Conversation>(`/api/conversations/${id}`));
  }
  history(id: string, before?: number) {
    return firstValueFrom(
      this.http.get<Message[]>(`/api/conversations/${id}/messages`, {
        params: before ? { before } : {},
      }),
    );
  }
  send(id: string, request: SendRequest) {
    return firstValueFrom(this.http.post<Message>(`/api/conversations/${id}/messages`, request));
  }
  read(id: string, sequence: number) {
    return firstValueFrom(this.http.put(`/api/conversations/${id}/read`, { sequence }));
  }
  async select(id?: string) {
    const old = this.active;
    this.active = id;
    if (this.hub?.state !== HubConnectionState.Connected) return;
    if (old) await this.hub.invoke('Leave', old).catch(() => {});
    if (id) await this.hub.invoke('Join', id).catch(() => {});
  }
  indicateTyping() {
    if (
      !this.active ||
      this.hub?.state !== HubConnectionState.Connected ||
      Date.now() - this.lastTyping < 3000
    )
      return;
    this.lastTyping = Date.now();
    void this.hub.invoke('Typing', this.active, true).catch(() => {});
  }
}
