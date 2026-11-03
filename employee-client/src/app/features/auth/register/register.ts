import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { readApiError } from '../../../core/api-error';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly formBuilder = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly form = this.formBuilder.nonNullable.group({
    fullName: ['', [Validators.required, Validators.minLength(2)]],
    email: ['', [Validators.required, Validators.email]],
    password: [
      '',
      [
        Validators.required,
        Validators.minLength(8),
        Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$/),
      ],
    ],
    confirmPassword: ['', Validators.required],
  });

  constructor() {
    if (this.auth.isAuthenticated()) {
      void this.router.navigate(['/dashboard']);
    }
  }

  protected submit(): void {
    const values = this.form.getRawValue();
    if (values.password !== values.confirmPassword) {
      this.form.controls.confirmPassword.setErrors({ mismatch: true });
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.auth
      .register(values)
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: response => {
          this.form.controls.password.reset();
          this.form.controls.confirmPassword.reset();
          if ('token' in response) void this.router.navigate(['/dashboard']);
          else this.error.set(response.message);
        },
        error: (error) =>
          this.error.set(readApiError(error, 'Unable to create the account. Please try again.')),
      });
  }
}
