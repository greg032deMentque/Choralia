import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '@core/auth.store';
import { ToastService } from '@app/services/toast.service';

export interface ApiClientErrorResponse {
  StatusCode?: number;
  TraceId?: string;
  Message?: string;
  Errors?: string[];
}

export function isAuthEndpointRequest(url: string): boolean {
  const lower = url.toLowerCase();
  return lower.includes('/api/auth/login')
    || lower.includes('/api/auth/refreshtoken')
    || lower.includes('/api/auth/forgotpassword')
    || lower.includes('/api/auth/resetpassword')
    || lower.includes('/api/auth/logout');
}

export function shouldLogoutOnUnauthorizedStatus(status: number, url: string): boolean {
  return status === 401 && !isAuthEndpointRequest(url);
}

function extractMessage(error: HttpErrorResponse): string {
  const body = error.error as ApiClientErrorResponse | undefined;
  if (body?.Message) return body.Message;
  if (body?.Errors && body.Errors.length > 0) return body.Errors.slice(0, 3).join(' • ');
  if (error.status === 0) return 'Connexion au serveur impossible. Vérifiez votre réseau.';
  if (error.status >= 500) return 'Une erreur serveur est survenue. Merci de réessayer.';
  return 'Une erreur est survenue.';
}

export const apiErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const authStore = inject(AuthStore);
  const toast = inject(ToastService);

  return next(req).pipe(
    catchError((err: HttpErrorResponse) => {
      if (shouldLogoutOnUnauthorizedStatus(err.status, req.url)) {
        authStore.clear();
        toast.error('Session expirée. Merci de vous reconnecter.');
        return throwError(() => err);
      }

      if (err.status !== 401) {
        toast.error(extractMessage(err));
      }

      return throwError(() => err);
    })
  );
};
