export interface AuthUser {
  id: string;
  fullName: string;
  email: string;
}

export interface AuthResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthUser;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest extends LoginRequest {
  fullName: string;
  confirmPassword: string;
}
