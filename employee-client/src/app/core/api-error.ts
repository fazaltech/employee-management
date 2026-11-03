import { HttpErrorResponse } from '@angular/common/http';

export function readApiError(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  const body = error.error as
    | { detail?: string; title?: string; errors?: Record<string, string[]> }
    | string
    | null;

  if (typeof body === 'string' && body.trim()) {
    return body;
  }
  if (body && typeof body === 'object') {
    const validationMessage = body.errors
      ? Object.values(body.errors).flat().join(' ')
      : null;
    return validationMessage || body.detail || body.title || fallback;
  }

  return fallback;
}
