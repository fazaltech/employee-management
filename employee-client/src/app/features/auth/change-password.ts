import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { readApiError } from '../../core/api-error';
@Component({imports: [FormsModule], template: `<section style="max-width:480px;margin:40px auto;background:white;padding:32px;border-radius:16px"><h1>Change your password</h1><p>Choose a new password to secure your account before continuing.</p><p role="alert">{{error()}}</p><form (ngSubmit)="save()"><label>Current password<input type="password" name="current" [(ngModel)]="current" autocomplete="current-password" required></label><label>New password<input type="password" name="next" [(ngModel)]="next" autocomplete="new-password" minlength="8" maxlength="128" required></label><label>Confirm new password<input type="password" name="confirm" [(ngModel)]="confirm" autocomplete="new-password" required></label><button [disabled]="busy()">Change password</button></form></section>`, styles: [`label{display:grid;gap:8px;margin:20px 0}input,button{padding:12px;border:1px solid #cbd5e1;border-radius:8px}button{background:#0f766e;color:white}`]})
export class ChangePassword {
  private auth = inject(AuthService); private router = inject(Router);
  current = ''; next = ''; confirm = ''; readonly busy = signal(false); readonly error = signal('');
  save() {if(this.next !== this.confirm) {this.error.set('Passwords do not match.'); return;} this.busy.set(true); this.error.set(''); this.auth.changePassword(this.current, this.next).subscribe({next: () => {this.current = this.next = this.confirm = ''; this.busy.set(false); void this.router.navigate(['/dashboard']);}, error: e => {this.busy.set(false); this.error.set(readApiError(e, 'Unable to change password.'));}});}
}
