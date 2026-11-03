import { Component, inject, signal, effect, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HrmService, Membership } from '../../core/services/hrm.service';
import { readApiError } from '../../core/api-error';

@Component({
  selector: 'app-membership-panel', imports: [FormsModule],
  template: `<details style="background:white;padding:24px;border-radius:16px;margin:24px 0"><summary>Organization access</summary>
    <p>Create accounts or manage existing organization access. Job titles do not grant permissions.</p>
    <form (ngSubmit)="create()" style="display:flex;gap:12px;flex-wrap:wrap;margin:16px 0">
      <input name="name" [(ngModel)]="name" placeholder="Full name" aria-label="Full name" required maxlength="160">
      <input name="email" type="email" [(ngModel)]="email" placeholder="Email" aria-label="Email" required maxlength="150">
      <select name="role" [(ngModel)]="role" aria-label="New account role">@for (r of roles; track r) {<option>{{r}}</option>}</select>
      <input name="password" type="password" [(ngModel)]="password" autocomplete="new-password" placeholder="Initial password" aria-label="Initial password" maxlength="128">
      <button>Create account</button>
    </form>
    <p>An initial password is required when email is disabled. Leave a password field blank when updating to keep the existing password. Shared accounts require platform management for profile or password changes.</p>
    <p role="status">{{message()}}</p>
    @for (m of members(); track m.userId) {
      <div style="display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin:16px 0">
        <span>{{m.fullName}} ({{m.email}})</span>
        @if (m.emailVerificationPending) { <small>Awaiting email verification via password setup</small> }
        <select [disabled]="m.role === 'OrganizationOwner'" aria-label="Organization role" [(ngModel)]="m.role">@for (r of roles; track r) {<option>{{r}}</option>}@if(m.role === 'OrganizationOwner') {<option>OrganizationOwner</option>}</select>
        <label><input type="checkbox" [disabled]="m.role === 'OrganizationOwner'" [(ngModel)]="m.isActive"> Active</label><button [disabled]="m.role === 'OrganizationOwner'" (click)="save(m)">Save access</button>
        <input aria-label="Account full name" [(ngModel)]="m.fullName" maxlength="160">
        <input type="password" aria-label="New password, leave blank to keep current password" autocomplete="new-password" [(ngModel)]="passwords[m.userId]" placeholder="New password (optional)" maxlength="128">
        <button (click)="saveAccount(m)">Save account</button>
      </div>
    }
    <button (click)="load()">Refresh access list</button>
  </details>`,
})
export class MembershipPanel {
  private readonly hrm = inject(HrmService);
  readonly changed = output<void>();
  protected readonly members = signal<Membership[]>([]); protected readonly message = signal('');
  protected readonly roles = ['HRAdmin', 'Manager', 'Employee'];
  protected name = ''; protected email = ''; protected role = 'Employee'; protected password = '';
  protected passwords: Record<string, string> = {};
  constructor() { effect(() => { this.hrm.organization(); this.load(); }); }
  protected load() {
    const id = this.hrm.organization()?.id;
    this.members.set([]);
    this.passwords = {};
    this.hrm.memberships().subscribe({next: m => { if (id === this.hrm.organization()?.id) this.members.set(m); }, error: e => this.message.set(readApiError(e, 'Unable to load memberships.'))});
  }
  protected save(member: Membership) {
    this.hrm.updateMembership(member).subscribe({next: () => {this.message.set('Access updated.'); this.changed.emit();}, error: e => this.message.set(readApiError(e, 'Unable to update access.'))});
  }
  protected create() {
    this.hrm.createAccount(this.name, this.email, this.role, this.password || undefined).subscribe({
      next: () => { this.password = ''; this.name = ''; this.email = ''; this.message.set('Account created.'); this.load(); },
      error: e => this.message.set(readApiError(e, 'Unable to create account.')),
    });
  }
  protected saveAccount(member: Membership) {
    this.hrm.updateAccount(member.userId, member.fullName, this.passwords[member.userId] || undefined).subscribe({
      next: () => { delete this.passwords[member.userId]; this.message.set('Account updated.'); },
      error: e => this.message.set(readApiError(e, 'Unable to update account.')),
    });
  }
}
