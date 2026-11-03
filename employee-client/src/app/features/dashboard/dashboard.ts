import { Component, computed, effect, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../../core/services/auth.service';
import { WorkspaceService } from '../../core/services/workspace.service';
import { DashboardData, DashboardService } from '../../core/services/dashboard.service';
import { readApiError } from '../../core/api-error';
import { Icon } from '../../shared/icon';
import { DashboardCharts } from './dashboard-charts';

@Component({selector: 'app-dashboard', imports: [RouterLink, DatePipe, DecimalPipe, Icon, DashboardCharts], templateUrl: './dashboard.html', styleUrl: './dashboard.scss'})
export class Dashboard {
  protected readonly auth = inject(AuthService);
  protected readonly workspace = inject(WorkspaceService);
  private readonly api = inject(DashboardService);
  protected readonly reports = inject(ActivatedRoute).snapshot.data['view'] === 'reports';
  protected readonly today = new Date();
  protected readonly data = signal<DashboardData | null>(null);
  protected readonly loading = signal(false); protected readonly error = signal('');
  protected readonly platformLoading = signal(false); protected readonly platformError = signal('');
  protected readonly platformData = signal<{total: number; active: number; suspended: number; owners: number} | null>(null);
  protected readonly refresh = signal(0);
  protected readonly allowed = computed(() => !this.reports || this.workspace.hrm.organization()?.role !== 'Employee');
  constructor() {
    effect(cleanup => {
      const org = this.workspace.hrm.organization(); this.refresh();
      this.data.set(null); this.error.set(''); this.loading.set(false);
      if (!org || !this.allowed()) return;
      this.loading.set(true);
      const request = this.api.load(org.id).subscribe({next: data => {this.data.set(data); this.loading.set(false);}, error: e => {this.error.set(readApiError(e, 'Unable to load the dashboard.')); this.loading.set(false);}});
      cleanup(() => request.unsubscribe());
    });
    effect(cleanup => {
      const platform = this.workspace.platform(); this.refresh(); this.platformData.set(null); this.platformError.set('');
      if (!platform || this.reports) return;
      this.platformLoading.set(true);
      const request = forkJoin({organizations: this.workspace.hrm.platformOrganizations(), owners: this.workspace.hrm.owners()}).subscribe({
        next: r => {this.platformData.set({total: r.organizations.length, active: r.organizations.filter(o => o.status === 'Active').length, suspended: r.organizations.filter(o => o.status === 'Suspended').length, owners: new Set(r.owners.map(o => o.userId)).size}); this.platformLoading.set(false);},
        error: e => {this.platformError.set(readApiError(e, 'Unable to load platform metrics.')); this.platformLoading.set(false);},
      }); cleanup(() => request.unsubscribe());
    });
  }
  protected reload() { this.refresh.update(v => v + 1); }
  protected initials(name: string) { return name.split(' ').filter(Boolean).slice(0,2).map(n => n[0]).join('').toUpperCase(); }
  protected exportReport() {
    const data = this.data(); if (!data) return;
    const rows = [['Work date (UTC)', 'Present', 'Absent', 'Leave'], ...data.weeklyAttendance.map(d => [d.date, d.present, d.absent, d.leave])];
    const csv = rows.map(row => row.map(value => '"' + String(value).replaceAll('"', '""') + '"').join(',')).join('\r\n');
    const url = URL.createObjectURL(new Blob([csv], {type: 'text/csv;charset=utf-8'}));
    const link = document.createElement('a'); link.href = url; link.download = 'weekly-attendance.csv'; link.click(); URL.revokeObjectURL(url);
  }
}
