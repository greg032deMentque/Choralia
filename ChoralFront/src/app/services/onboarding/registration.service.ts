import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { IRegisterRequest } from '@models/onboarding-models/register-request.model';
import { IRegistrationResult } from '@models/onboarding-models/registration-result.model';
import { IResendVerificationRequest } from '@models/onboarding-models/resend-verification-request.model';

const AUTH_BASE_URL = `${environment.apiUrl}auth`;

// Routes /api/auth/Register, /VerifyEmail, /ResendVerification — domaine "auth" côté back
// (Chorale.ViewModels.Auth) mais regroupées ici dans le domaine front "onboarding" (inscription
// auto-service), périmètre de ce lot, plutôt que d'étendre AuthService (hors périmètre
// autorisé — voir écarts assumés du récapitulatif de génération).
@Injectable({ providedIn: 'root' })
export class RegistrationService {
  private readonly http = inject(HttpClient);

  // Réponse UNIQUE quel que soit le cas réel (anti-énumération, cf. IRegistrationResult) —
  // ne jamais interpréter le contenu de Message pour brancher un comportement différent.
  register(request: IRegisterRequest): Observable<IRegistrationResult> {
    return this.http.post<IRegistrationResult>(`${AUTH_BASE_URL}/Register`, request);
  }

  // 204 en succès. En échec, le back renvoie une erreur générique (message neutre) — voir
  // verify-email.component pour l'affichage.
  verifyEmail(userId: string, token: string): Observable<unknown> {
    const params = new HttpParams().set('userId', userId).set('token', token);
    return this.http.get<unknown>(`${AUTH_BASE_URL}/VerifyEmail`, { params });
  }

  // 204 toujours (invariant anti-énumération) — ne jamais afficher un message différent selon
  // que l'email existe ou non.
  resendVerification(request: IResendVerificationRequest): Observable<unknown> {
    return this.http.post<unknown>(`${AUTH_BASE_URL}/ResendVerification`, request);
  }
}
