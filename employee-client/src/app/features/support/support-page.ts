import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { AuthService } from '../../core/services/auth.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { environment } from '../../../environments/environment';

interface Conversation { id: string; organizationName: string; userName: string; updatedAtUtc: string; }
interface Message { id: number; conversationId: string; senderId: string; senderName: string; fromAdmin: boolean; text: string; sentAtUtc: string; }

@Component({
  selector: 'app-support-page', imports: [FormsModule, DatePipe],
  templateUrl: './support-page.html', styleUrl: './support-page.scss',
})
export class SupportPage {
  readonly auth = inject(AuthService);
  readonly workspace = inject(WorkspaceService);
  readonly allowed = computed(() => this.workspace.platform() || ['OrganizationOwner', 'Manager'].includes(this.workspace.hrm.organization()?.role ?? ''));
  readonly threads = signal<Conversation[]>([]);
  readonly selected = signal<string | null>(null);
  readonly current = computed(() => this.threads().find(t => t.id === this.selected()));
  readonly messages = signal<Message[]>([]);
  readonly status = signal('Connecting…');
  readonly connected = signal(false);
  readonly busy = signal(false);
  readonly loadingOlder = signal(false);
  readonly hasOlder = signal(false);
  readonly error = signal('');
  readonly unread = signal<Set<string>>(new Set());
  readonly drafts: Record<string, string> = {};
  private connection?: HubConnection;
  private refreshVersion = 0;
  private historyVersion = 0;
  get draft() { return this.drafts[this.selected() ?? ''] ?? ''; }
  set draft(value: string) { this.drafts[this.selected() ?? ''] = value; }

  constructor() {
    effect(cleanup => {
      const user = this.auth.user();
      const allowed = this.allowed();
      this.workspace.hrm.organization()?.id;
      this.threads.set([]); this.messages.set([]); this.selected.set(null); this.unread.set(new Set());
      this.connected.set(false); this.error.set('');
      this.refreshVersion++; this.historyVersion++;
      if (!user || !allowed) return;
      const connection = new HubConnectionBuilder()
        .withUrl(`${environment.apiUrl.replace(/\/api\/?$/, '')}/hubs/support`, { accessTokenFactory: () => this.auth.token ?? '', withCredentials: false })
        .withAutomaticReconnect().build();
      this.connection = connection;
      connection.on('SupportChanged', () => void this.refresh(connection));
      connection.onreconnecting(() => { this.connected.set(false); this.status.set('Reconnecting…'); });
      connection.onreconnected(() => { this.messages.set([]); this.connected.set(true); this.status.set('Connected'); void this.refresh(connection); });
      connection.onclose(() => { if (this.connection === connection) { this.connected.set(false); this.status.set('Disconnected'); } });
      void this.start(connection);
      cleanup(() => { if (this.connection === connection) this.connection = undefined; void connection.stop(); });
    });
  }

  async reconnect() { if (this.connection) await this.start(this.connection); }
  private async start(connection: HubConnection) {
    this.status.set('Connecting…'); this.error.set('');
    try {
      await connection.start();
      if (this.connection !== connection) return;
      this.messages.set([]);
      this.connected.set(true); this.status.set('Connected'); await this.refresh(connection);
    } catch { if (this.connection === connection) { this.status.set('Disconnected'); this.error.set('Unable to connect to support. Check your connection and account access, then retry.'); } }
  }

  private async refresh(connection: HubConnection) {
    const version = ++this.refreshVersion;
    try {
      const threads = await connection.invoke<Conversation[]>('ListConversations');
      if (this.connection !== connection || version !== this.refreshVersion) return;
      const old = this.threads();
      this.unread.update(values => {
        const next = new Set(values);
        for (const t of threads) if (t.id !== this.selected() && old.length && old.find(o => o.id === t.id)?.updatedAtUtc !== t.updatedAtUtc) next.add(t.id);
        return next;
      });
      this.threads.set(threads);
      if (!this.selected() && threads.length) await this.select(threads[0].id);
      else if (this.selected()) await this.loadMessages(this.selected()!, false);
    } catch { if (this.connection === connection && version === this.refreshVersion) { this.error.set('Unable to load support. Your access may have changed.'); this.messages.set([]); this.threads.set([]); this.selected.set(null); } }
  }

  async select(id: string) {
    this.selected.set(id); this.messages.set([]); this.hasOlder.set(false); this.error.set('');
    this.unread.update(values => { const next = new Set(values); next.delete(id); return next; });
    await this.loadMessages(id, false);
  }

  async older() { if (this.selected()) await this.loadMessages(this.selected()!, true); }
  private async loadMessages(id: string, older: boolean) {
    const connection = this.connection;
    if (!connection) return;
    const version = ++this.historyVersion;
    this.loadingOlder.set(older);
    try {
      const page = await connection.invoke<Message[]>('GetMessages', id, older ? this.messages()[0]?.id ?? null : null);
      if (this.connection !== connection || this.selected() !== id || version !== this.historyVersion) return;
      // Keep previously loaded history while merging live updates without duplicate messages.
      this.messages.update(items => {
        // If more than one page arrived while offline, restart at the latest page so older paging has no gap.
        const previous = !older && page.length === 50 && items.length && !page.some(m => items.some(i => i.id === m.id)) ? [] : items;
        return Array.from(new Map([...previous, ...page].map(m => [m.id, m])).values()).sort((a, b) => a.id - b.id);
      });
      if (older || this.messages().length <= 50) this.hasOlder.set(page.length === 50);
      if (!older) setTimeout(() => document.getElementById('support-bottom')?.scrollIntoView({ block: 'nearest' }));
    } catch { if (this.selected() === id && this.connection === connection) { this.messages.set([]); this.error.set('Unable to load this conversation. Check your access or reconnect.'); } }
    finally { if (version === this.historyVersion) this.loadingOlder.set(false); }
  }

  async send() {
    const id = this.selected(), connection = this.connection, text = this.draft.trim();
    if (!id || !connection || !this.connected() || this.busy() || !text || text.length > 4000) return;
    this.busy.set(true); this.error.set('');
    try {
      await connection.invoke<Message>('SendMessage', id, text);
      if (this.drafts[id]?.trim() === text) this.drafts[id] = '';
      await this.refresh(connection);
    } catch { this.error.set('Message could not be confirmed. Reconnect and check the conversation before retrying. Your draft is saved.'); }
    finally { this.busy.set(false); }
  }
}
