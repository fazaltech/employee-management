import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DashboardData } from '../../core/services/dashboard.service';
import { Icon } from '../../shared/icon';

@Component({selector: 'app-dashboard-charts', imports: [DatePipe, RouterLink, Icon], templateUrl: './dashboard-charts.html', styleUrl: './dashboard-charts.scss'})
export class DashboardCharts {
  readonly data = input.required<DashboardData>();
  protected readonly chartMax = computed(() => Math.max(1, ...this.data().weeklyAttendance.map(d => d.present + d.absent + d.leave)));
  protected readonly departmentMax = computed(() => Math.max(1, ...this.data().departments.map(d => d.employees)));
}
