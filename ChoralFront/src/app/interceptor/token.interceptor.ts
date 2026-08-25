import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthStore } from '@core/auth.store';
import { StorageService } from '@app/services/storage.service';
import { ToastService } from '@app/services/toast.service';
import { AuthService } from '@app/services/auth/auth.service';
import { IToken } from '@models/auth-models/token.model';
import { isTokenExpired } from '@core/jwt.util';
import { DisplayedZoneStore } from '@core/displayed-zone.store';

function addAuth(req: HttpRequest<unknown>, token?: string | null): HttpRequest<unknown> {
  return token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;
}

// Header X-Space-Id : porte le scope d'espace actif (chorale ou événement) sur toute
// requête sortante non-auth (ChoralFront/CLAUDE.md — Rôles, chorale actif et guards ;
// étendu au lot 4 zones). Absent si aucun espace actif n'est encore sélectionné (ex. avant
// le premier GET /Me) ou si la zone courante est /admin (zone globale, jamais scopée).
function addSpaceScope(req: HttpRequest<unknown>, spaceId: string | null): HttpRequest<unknown> {
  return spaceId ? req.clone({ setHeaders: { 'X-Space-Id': spaceId } }) : req;
}

export const tokenInterceptor: HttpInterceptorFn = (req, next) => {
  const storage = inject(StorageService);
  const authStore = inject(AuthStore);
  const authService = inject(AuthService);
  const toast = inject(ToastService);
  const displayedZoneStore = inject(DisplayedZoneStore);

  // /api/auth/logout est volontairement ABSENT de cette liste : AuthController.Logout porte
  // [Authorize(Bearer)]. Sans le token, l'appel repartait en 401 et accountService.Logout
  // n'était jamais exécuté — le refresh token restait valide côté serveur (OWASP A07).
  // ApiErrorInterceptor, lui, garde /logout dans sa propre liste : un 401 sur cette route ne
  // doit pas déclencher un second clear() ni un toast « session expirée ».
  const isAuthEndpoint = req.url.toLowerCase().includes('/api/auth/login')
    || req.url.toLowerCase().includes('/api/auth/refreshtoken')
    || req.url.toLowerCase().includes('/api/auth/forgotpassword')
    || req.url.toLowerCase().includes('/api/auth/resetpassword');

  if (isAuthEndpoint) return next(req);

  // Le scope d'espace suit la zone AFFICHÉE (URL couramment rendue), jamais un repli sur
  // l'ensemble des rôles de l'utilisateur (core/displayed-zone.ts) — corrige deux défauts
  // réels de l'ancienne lecture via AuthStore.currentZone() (toujours 'admin' pour un Admin
  // global, quelle que soit l'URL affichée) : (1) un Admin naviguant dans /management/:spaceId
  // ne recevait jamais X-Space-Id, la priorité Admin de resolveZone() écrasant tout ; (2) un
  // ClientManager+Manager naviguant dans /client/:clientId recevait à tort le X-Space-Id de
  // son espace de gestion (resolveZone() plaçait 'management' avant 'client').
  // /management/:spaceId : le spaceId vient de l'URL — fiable même quand spaceRoleGuard ne
  // synchronise pas AuthStore.activeSpaceId (cas d'un Admin global, qui court-circuite le
  // guard avant l'appel à setActiveSpace). /me ne porte aucun spaceId dans l'URL : seul
  // AuthStore.activeSpaceId (espace choisi via le sélecteur de la topbar) sait quel espace
  // membre afficher. Toute autre zone (admin, client, no-space) : aucun scope d'espace.
  const zone = displayedZoneStore.zone();
  const spaceScopeId =
    zone.kind === 'management' ? (zone.spaceId ?? null) : zone.kind === 'member' ? authStore.activeSpaceId() : null;
  const scopedReq = addSpaceScope(req, spaceScopeId);

  const token = storage.GetToken();
  if (token && !isTokenExpired(token)) {
    return next(addAuth(scopedReq, token));
  }

  const refreshToken = storage.GetRefreshToken();
  if (!token || !refreshToken) {
    return next(scopedReq);
  }

  const refreshPayload: IToken = { AccessToken: token, RefreshToken: refreshToken, DeviceId: storage.GetDeviceId() };

  return authService.refreshToken(refreshPayload).pipe(
    switchMap(res => next(addAuth(scopedReq, res.AccessToken))),
    catchError((err: HttpErrorResponse) => {
      authStore.clear();
      toast.error('Votre session a expiré, merci de vous reconnecter.');
      return throwError(() => err);
    })
  );
};
