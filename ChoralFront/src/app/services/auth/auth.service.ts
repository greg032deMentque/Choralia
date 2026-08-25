import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, switchMap, tap } from 'rxjs';
import { environment } from '@env/environment';
import { AuthStore } from '@core/auth.store';
import { StorageService } from '@app/services/storage.service';
import { IToken } from '@models/auth-models/token.model';
import { ILoginRequest } from '@models/auth-models/login-request.model';
import { ILogoutRequest } from '@models/auth-models/logout-request.model';
import { IResetPasswordRequest } from '@models/auth-models/reset-password-request.model';
import { IActivateAccountRequest } from '@models/auth-models/activate-account-request.model';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';

const AUTH_BASE_URL = `${environment.apiUrl}auth`;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly storage = inject(StorageService);

  login(request: ILoginRequest): Observable<IAuthenticatedUser> {
    return this.http.post<IToken>(`${AUTH_BASE_URL}/Login`, request).pipe(
      tap(token => this.authStore.setSession(token)),
      switchMap(() => this.getCurrentUser()),
      tap(user => this.authStore.setCurrentUser(user))
    );
  }

  refreshToken(request: IToken): Observable<IToken> {
    return this.http.post<IToken>(`${AUTH_BASE_URL}/RefreshToken`, request).pipe(
      tap(token => this.authStore.setSession(token))
    );
  }

  logout(request: ILogoutRequest): Observable<unknown> {
    return this.http.post<unknown>(`${AUTH_BASE_URL}/Logout`, request).pipe(
      tap(() => this.authStore.clear())
    );
  }

  forgotPassword(email: string): Observable<unknown> {
    return this.http.post<unknown>(`${AUTH_BASE_URL}/ForgotPassword`, JSON.stringify(email), {
      headers: { 'Content-Type': 'application/json' }
    });
  }

  resetPassword(request: IResetPasswordRequest): Observable<unknown> {
    return this.http.post<unknown>(`${AUTH_BASE_URL}/ResetPassword`, request);
  }

  // Activation du compte d'un membre invité : pose son mot de passe et confirme son email
  // (204). Route anonyme et limitée en débit côté back (10 requêtes/heure/IP) — aucun token
  // n'est renvoyé, l'utilisateur passe ensuite par l'écran de connexion.
  activateAccount(request: IActivateAccountRequest): Observable<unknown> {
    return this.http.post<unknown>(`${AUTH_BASE_URL}/ActivateAccount`, request);
  }

  getCurrentUser(): Observable<IAuthenticatedUser> {
    return this.http.get<IAuthenticatedUser>(`${AUTH_BASE_URL}/Me`);
  }

  // Appelé une seule fois au démarrage de l'app (provideAppInitializer, app.config.ts).
  // Si un token existe déjà en sessionStorage (session en cours, ex. reload de page),
  // repeuple AuthStore.user via GET /Me. Sinon, ne fait rien (utilisateur non connecté).
  initializeSession(): Observable<void> {
    const token = this.storage.GetToken();
    if (!token) return of(void 0);
    return this.getCurrentUser().pipe(
      tap(user => this.authStore.setCurrentUser(user)),
      switchMap(() => of(void 0)),
      catchError(() => {
        this.authStore.clear();
        return of(void 0);
      })
    );
  }
}
