import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { readApiError } from '../../../core/api-error';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.formBuilder.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  constructor() {
    if (this.auth.isAuthenticated()) {
      void this.router.navigate(['/dashboard']);
    }
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.auth
      .login(this.form.getRawValue())
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => {
          const requestedUrl = this.route.snapshot.queryParamMap.get('returnUrl');
          const returnUrl = requestedUrl?.startsWith('/') ? requestedUrl : '/dashboard';
          void this.router.navigateByUrl(returnUrl);
        },
        error: (error) =>
          this.error.set(readApiError(error, 'Unable to sign in. Please try again.')),
      });
  }
}
