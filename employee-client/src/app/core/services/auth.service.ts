import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AuthResponse,
  AuthUser,
  LoginRequest,
  RegisterRequest,
} from '../models/auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly tokenKey = 'peopleops_token';
  private readonly userKey = 'peopleops_user';
  private readonly expiryKey = 'peopleops_expiry';
  private readonly userSignal = signal<AuthUser | null>(this.readStoredUser());

  readonly user = this.userSignal.asReadonly();
  readonly isAuthenticated = computed(
    () => this.userSignal() !== null && this.hasValidToken(),
  );

  get token(): string | null {
    return this.hasValidToken() ? localStorage.getItem(this.tokenKey) : null;
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${environment.apiUrl}/auth/login`, request)
      .pipe(tap((response) => this.storeAuth(response)));
  }

  register(request: RegisterRequest): Observable<AuthResponse | { requiresEmailConfirmation: boolean; message: string }> {
    return this.http
      .post<AuthResponse | { requiresEmailConfirmation: boolean; message: string }>(`${environment.apiUrl}/auth/register`, request)
      .pipe(tap((response) => { if ('token' in response) this.storeAuth(response); }));
  }

  logout(): void {
    this.clearStoredAuth();
    this.userSignal.set(null);
    void this.router.navigate(['/login']);
  }

  private storeAuth(response: AuthResponse): void {
    localStorage.setItem(this.tokenKey, response.token);
    localStorage.setItem(this.userKey, JSON.stringify(response.user));
    localStorage.setItem(this.expiryKey, response.expiresAtUtc);
    this.userSignal.set(response.user);
  }

  private readStoredUser(): AuthUser | null {
    if (!this.hasValidToken()) {
      this.clearStoredAuth();
      return null;
    }

    try {
      const value = localStorage.getItem(this.userKey);
      return value ? (JSON.parse(value) as AuthUser) : null;
    } catch {
      this.clearStoredAuth();
      return null;
    }
  }

  private hasValidToken(): boolean {
    const token = localStorage.getItem(this.tokenKey);
    const expiry = localStorage.getItem(this.expiryKey);
    return Boolean(token && expiry && new Date(expiry).getTime() > Date.now());
  }

  private clearStoredAuth(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.userKey);
    localStorage.removeItem(this.expiryKey);
  }
}
