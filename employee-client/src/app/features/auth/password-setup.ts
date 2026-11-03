import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize, Observable } from 'rxjs';
import { HrmService } from '../../core/services/hrm.service';
import { readApiError } from '../../core/api-error';

@Component({
  selector: 'app-password-setup', imports: [FormsModule, RouterLink],
  template: `<section style="max-width:480px;margin:4rem auto;padding:2rem;background:white;border-radius:16px">
    <h1>Set your password</h1>
    <p>Request a secure link using the email address registered for your account.</p>
    <form (ngSubmit)="submit()">
      @if (token) {
        <label>New password <input name="password" type="password" [(ngModel)]="password" required minlength="8" maxlength="128" autocomplete="new-password"></label>
        <p>Use at least 8 characters, including uppercase, lowercase, and a digit.</p>
      } @else {
        <label>Email <input name="email" type="email" [(ngModel)]="email" required autocomplete="email"></label>
      }
      <button [disabled]="busy()">{{ token ? 'Set password' : 'Email setup link' }}</button>
    </form>
    <p role="status">{{ message() }}</p><a routerLink="/login">Back to sign in</a>
  </section>`,
})
export class PasswordSetup {
  private readonly hrm = inject(HrmService);
  private readonly fragment = new URLSearchParams(window.location.hash.slice(1));
  protected token = this.fragment.get('token') ?? '';
  private readonly userId = this.fragment.get('userId') ?? '';
  protected email = ''; protected password = '';
  protected readonly message = signal(''); protected readonly busy = signal(false);
  constructor() { if (this.token) window.history.replaceState(null, '', window.location.pathname); }
  protected submit() {
    this.busy.set(true); this.message.set('');
    const completing = !!this.token;
    const request: Observable<void | {message: string}> = completing ? this.hrm.setupPassword(this.userId, this.token, this.password) : this.hrm.setupEmail(this.email);
    request.pipe(finalize(() => this.busy.set(false))).subscribe({
      next: result => { this.message.set(completing ? 'Password saved. You can now sign in.' : (result?.message ?? 'Request completed.')); this.password = ''; },
      error: e => this.message.set(readApiError(e, 'Unable to set up password.')),
    });
  }
}
