import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HrmService, Organization, OwnerAccount } from '../../core/services/hrm.service';
import { readApiError } from '../../core/api-error';

@Component({
  selector: 'app-platform-owners-panel', imports: [FormsModule],
  template: `@if (allowed()) {
    <details style="background:white;padding:24px;margin:24px 0;border-radius:16px"><summary>Platform: organizations & owners</summary>
      <p>Initial passwords are required when email is disabled. Leave update passwords blank to retain existing passwords.</p>
      <p role="status">{{message()}}</p>
      <form (ngSubmit)="create()" style="display:flex;gap:12px;flex-wrap:wrap;margin:16px 0">
        <input name="organization" [(ngModel)]="organizationId" required maxlength="160" placeholder="Organization name" aria-label="Organization name">
        <input name="name" [(ngModel)]="name" placeholder="Owner name" aria-label="Owner name" required maxlength="160">
        <input name="email" type="email" [(ngModel)]="email" placeholder="Owner email" aria-label="Owner email" required maxlength="150">
        <input name="password" type="password" [(ngModel)]="password" autocomplete="new-password" placeholder="Initial password" aria-label="Initial password" maxlength="128">
        <button>Create organization & owner</button>
      </form>
      @for (o of organizations(); track o.id) {<div style="display:flex;gap:12px;margin:16px 0"><input [(ngModel)]="o.name" aria-label="Organization name"><span>{{o.status}}</span><button (click)="rename(o)">Save name</button><button (click)="toggle(o)">{{o.status === 'Active' ? 'Deactivate' : 'Activate'}}</button></div>}
      @for (owner of owners(); track owner.organizationId + owner.userId) {
        <div style="display:flex;gap:12px;flex-wrap:wrap;margin:16px 0">
          <span>{{owner.organizationName}} / {{owner.email}}</span>
          <input [(ngModel)]="owner.fullName" aria-label="Owner full name" maxlength="160">
          <input type="password" [(ngModel)]="passwords[owner.userId]" autocomplete="new-password" placeholder="New password (optional)" aria-label="New password, leave blank to keep current password" maxlength="128">
          <button (click)="update(owner)">Update owner</button>
        </div>
      }
      <button (click)="load()">Refresh owners and organizations</button>
    </details>
  }`,
})
export class PlatformOwnersPanel {
  private readonly hrm = inject(HrmService);
  protected readonly allowed = signal(false); protected readonly message = signal('');
  protected readonly organizations = signal<Organization[]>([]); protected readonly owners = signal<OwnerAccount[]>([]);
  protected organizationId = ''; protected name = ''; protected email = ''; protected password = '';
  protected passwords: Record<string, string> = {};
  constructor() { this.hrm.accountOptions().subscribe({next: o => {this.allowed.set(o.isPlatformAdmin); if (o.isPlatformAdmin) this.load();}}); }
  protected load() {
    this.passwords = {};
    this.hrm.platformOrganizations().subscribe({next: o => this.organizations.set(o), error: e => this.message.set(readApiError(e, 'Unable to load organizations.'))});
    this.hrm.owners().subscribe({next: o => this.owners.set(o), error: e => this.message.set(readApiError(e, 'Unable to load owners.'))});
  }
  protected create() {
    this.hrm.createPlatformOrganization(this.organizationId, this.name, this.email, this.password || undefined).subscribe({
      next: () => {this.password = ''; this.name = ''; this.email = ''; this.message.set('Owner account created.'); this.load();},
      error: e => this.message.set(readApiError(e, 'Unable to create owner.')),
    });
  }
  protected rename(o: Organization) {this.hrm.platformRename(o).subscribe({next: () => this.load(), error: e => this.message.set(readApiError(e, 'Unable to rename organization.'))});}
  protected toggle(o: Organization) {this.hrm.setStatus(o.id, o.status === 'Active' ? 'Suspended' : 'Active').subscribe({next: () => this.load(), error: e => this.message.set(readApiError(e, 'Unable to update status.'))});}
  protected update(owner: OwnerAccount) {
    this.hrm.updateOwner(owner, this.passwords[owner.userId] || undefined).subscribe({
      next: () => {delete this.passwords[owner.userId]; this.message.set('Owner account updated.');},
      error: e => this.message.set(readApiError(e, 'Unable to update owner.')),
    });
  }
}
