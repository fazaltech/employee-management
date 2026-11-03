import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { AuthService } from './auth.service';
import { HrmService, Organization } from './hrm.service';
import { readApiError } from '../api-error';

@Injectable({ providedIn: 'root' })
export class WorkspaceService {
  readonly hrm = inject(HrmService);
  private readonly auth = inject(AuthService);
  readonly organizations = signal<Organization[]>([]);
  readonly platform = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly isHR = computed(() => ['OrganizationOwner', 'HRAdmin'].includes(this.hrm.organization()?.role ?? ''));
  readonly isOwner = computed(() => this.hrm.organization()?.role === 'OrganizationOwner');
  readonly role = computed(() => this.hrm.organization()?.role.replace(/([a-z])([A-Z])/g, '$1 $2') ?? (this.platform() ? 'Platform Admin' : 'No organization'));
  private generation = 0;
  constructor() {
    effect(() => {
      const user = this.auth.user();
      this.generation++;
      this.hrm.organization.set(null); this.organizations.set([]); this.platform.set(false); this.error.set('');
      if (user) this.refresh(); else this.loading.set(false);
    });
  }
  refresh() {
    const version = ++this.generation;
    this.loading.set(true); this.error.set('');
    forkJoin({ organizations: this.hrm.organizations(), options: this.hrm.accountOptions() }).subscribe({
      next: result => {
        if (version !== this.generation) return;
        this.organizations.set(result.organizations); this.platform.set(result.options.isPlatformAdmin);
        const selected = result.organizations.find(o => o.id === this.hrm.organization()?.id) ?? result.organizations[0] ?? null;
        this.hrm.organization.set(selected); this.loading.set(false);
      },
      error: e => { if (version === this.generation) {this.loading.set(false); this.error.set(readApiError(e, 'Unable to load your workspace.'));} },
    });
  }
  select(id: string) { this.hrm.organization.set(this.organizations().find(o => o.id === id) ?? null); }
}
