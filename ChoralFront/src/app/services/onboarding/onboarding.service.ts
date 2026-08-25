import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, switchMap, tap } from 'rxjs';
import { environment } from '@env/environment';
import { AuthStore } from '@core/auth.store';
import { AuthService } from '@app/services/auth/auth.service';
import { buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IPreviewCode } from '@models/onboarding-models/preview-code.model';
import { IRequestMembershipRequest } from '@models/onboarding-models/request-membership-request.model';
import { IMyRequest } from '@models/onboarding-models/my-request.model';
import { ICreateChoirRequest } from '@models/onboarding-models/create-choir-request.model';
import { IChoirCreationResult } from '@models/onboarding-models/choir-creation-result.model';
import { ICreateEventRequest } from '@models/onboarding-models/create-event-request.model';
import { IEvent } from '@models/events-models/event.model';

const ONBOARDING_BASE_URL = `${environment.apiUrl}onboarding`;

@Injectable({ providedIn: 'root' })
export class OnboardingService {
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthStore);
  private readonly authService = inject(AuthService);

  // Public, sans authentification. En échec (code inconnu/expiré), le back renvoie le message
  // unique "Code inconnu ou expiré." — ne jamais l'enrichir ni deviner la cause (décision
  // produit, anti-énumération). Un 429 ("Trop de tentatives…") est propagé tel quel.
  previewCode(code: string): Observable<IPreviewCode> {
    const params = new HttpParams().set('code', code);
    return this.http.get<IPreviewCode>(`${ONBOARDING_BASE_URL}/PreviewCode`, { params });
  }

  requestMembership(request: IRequestMembershipRequest): Observable<IMyRequest> {
    return this.http.post<IMyRequest>(`${ONBOARDING_BASE_URL}/RequestMembership`, request);
  }

  mesRequests(pagination: IPaginationQueryParams): Observable<IPaginatedResult<IMyRequest>> {
    const params = buildPaginationParams(pagination);
    return this.http.get<IPaginatedResult<IMyRequest>>(`${ONBOARDING_BASE_URL}/MyRequests`, { params });
  }

  cancelRequest(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${ONBOARDING_BASE_URL}/MyRequests/${id}`);
  }

  // Après création, l'utilisateur devient Responsable du nouvel espace : AuthStore.user
  // (peuplé à la connexion) ne le sait pas encore. On rafraîchit la session (GET /api/auth/Me)
  // avant de renvoyer le résultat, sinon spaceRoleGuard rejetterait l'accès à
  // /management/:nouvelEspaceId juste après la création (même pattern que AuthService.login).
  createChoir(request: ICreateChoirRequest): Observable<IChoirCreationResult> {
    return this.http.post<IChoirCreationResult>(`${ONBOARDING_BASE_URL}/CreateChoir`, request).pipe(
      switchMap(result =>
        this.authService.getCurrentUser().pipe(
          tap(user => this.authStore.setCurrentUser(user)),
          switchMap(() => {
            if (result.Id) this.authStore.setActiveSpace(result.Id);
            return [result];
          })
        )
      )
    );
  }

  // Réponse = EventViewModel (back) : ChoirId est nul ici (événement autonome), ClientId
  // porte le client de rattachement — voir IEvent (models/events-models). Ancien modèle
  // dédié IEvenementCreationResult retiré (correction ciblée) : il dupliquait IEvent, devenu
  // correctement nullable sur ChoirId.
  createEvent(request: ICreateEventRequest): Observable<IEvent> {
    return this.http.post<IEvent>(`${ONBOARDING_BASE_URL}/CreateEvent`, request).pipe(
      switchMap(result =>
        this.authService.getCurrentUser().pipe(
          tap(user => this.authStore.setCurrentUser(user)),
          switchMap(() => {
            if (result.Id) this.authStore.setActiveSpace(result.Id);
            return [result];
          })
        )
      )
    );
  }
}
