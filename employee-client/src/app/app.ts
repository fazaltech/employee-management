import { Component, HostListener, computed, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from './core/services/auth.service';
import { WorkspaceService } from './core/services/workspace.service';
import { DashboardService } from './core/services/dashboard.service';
import { readApiError } from './core/api-error';
import { Icon } from './shared/icon';

@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet, FormsModule, Icon],
  selector: 'app-root', styleUrl: './app.scss', templateUrl: './app.html',
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly router = inject(Router);
  private readonly dashboard = inject(DashboardService);
  protected readonly collapsed = signal(false);
  protected readonly mobile = signal(window.innerWidth < 960);
  protected readonly drawer = signal(false);
  protected readonly notifications = signal(false);
  protected readonly mobileSearch = signal(false);
  protected readonly notices = signal<{label: string; count: number; destination: string}[]>([]);
  protected readonly noticeMessage = signal('');
  protected query = '';
  protected readonly nav = computed(() => {
    const scoped = !!this.workspace.hrm.organization();
    const items = [{label: 'Dashboard', path: '/dashboard', icon: 'grid'}];
    if (scoped) items.push({label: 'Employees', path: '/employees', icon: 'people'}, {label: 'Departments', path: '/departments', icon: 'building'});
    if (scoped && this.workspace.hrm.organization()?.role !== 'Employee') items.push({label: 'Organization Hierarchy', path: '/hierarchy', icon: 'hierarchy'});
    if (scoped) items.push({label: 'Attendance', path: '/attendance', icon: 'clock'});
    if (scoped && this.workspace.hrm.organization()?.role !== 'Employee') items.push({label: 'Reports', path: '/reports', icon: 'chart'});
    items.push({label: 'Settings', path: '/settings', icon: 'settings'});
    return items;
  });
  constructor() {
    this.router.events.pipe(takeUntilDestroyed()).subscribe(event => { if (event instanceof NavigationEnd) { this.drawer.set(false); this.notifications.set(false); } });
    effect(() => { this.workspace.hrm.organization(); this.notifications.set(false); this.notices.set([]); this.query = ''; });
  }
  @HostListener('window:resize') onResize() { this.mobile.set(window.innerWidth < 960); }
  @HostListener('document:keydown', ['$event']) onKey(event: KeyboardEvent) {
    if (event.key === 'Escape') { this.closeDrawer(); this.notifications.set(false); }
    if (event.key === 'Tab' && this.mobile() && this.drawer()) {
      const nodes = Array.from(document.querySelectorAll<HTMLElement>('.sidebar a, .sidebar button'));
      const first = nodes[0], last = nodes[nodes.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    }
  }
  protected toggleSidebar() {
    if (this.mobile()) { this.drawer.update(x => !x); if (this.drawer()) setTimeout(() => document.querySelector<HTMLElement>('.sidebar a')?.focus()); }
    else this.collapsed.update(x => !x);
  }
  protected closeDrawer() { if (this.drawer()) { this.drawer.set(false); document.getElementById('sidebar-toggle')?.focus(); } }
  protected search() { if (this.workspace.hrm.organization()) { this.mobileSearch.set(false); void this.router.navigate(['/employees'], {queryParams: {q: this.query.trim() || null}}); } }
  protected showNotifications() {
    this.notifications.update(x => !x);
    if (!this.notifications()) return;
    const id = this.workspace.hrm.organization()?.id;
    const userId = this.auth.user()?.id;
    this.notices.set([]);
    if (!id) {this.noticeMessage.set('Select an organization to see pending actions.'); return;}
    this.noticeMessage.set('Loading pending actions...');
    this.dashboard.load(id).subscribe({
      next: data => {if (id === this.workspace.hrm.organization()?.id && userId === this.auth.user()?.id) {this.notices.set(data.actions.filter(x => x.count)); this.noticeMessage.set(data.pendingActions ? '' : 'You are all caught up. No pending actions.');}},
      error: e => {if (id === this.workspace.hrm.organization()?.id && userId === this.auth.user()?.id) this.noticeMessage.set(readApiError(e, 'Unable to load pending actions.'));},
    });
  }
  protected logout() { this.auth.logout(); }
}
