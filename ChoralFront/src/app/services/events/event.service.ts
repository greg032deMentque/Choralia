import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import { IEvent } from '@models/events-models/event.model';
import { EventStatusEnum } from '@app/enums/event-status.enum';

const EVENTS_BASE_URL = `${environment.apiUrl}events`;



@Injectable({ providedIn: 'root' })
export class EventService {
  private readonly http = inject(HttpClient);

  // choirId explicitement requis : EventController.GetPaged ne scope pas
  // automatiquement par X-Chorale-Id (contrairement au header porté par tokenInterceptor),
  // le filtre ChoirId doit donc être transmis pour éviter une fuite de données
  // inter-chorales (écart documenté au bloc de transfert Phase 1 — choix sécurisé).
  getPaged(choirId: string, params: IPaginationQueryParams): Observable<IPaginatedResult<IEvent>> {
    const httpParams = buildPaginationParams(params).set('ChoirId', choirId);
    return this.http
      .post<IPaginatedResult<IEvent>>(`${EVENTS_BASE_URL}/GetPaged`, null, { params: httpParams })
  }

  // Événements à venir de l'utilisateur courant, TOUS espaces confondus. Aucun ChoirId n'est
  // transmis, volontairement : EventService.GetPagedAsync (back) restreint déjà un non-admin
  // aux espaces où il est membre Actif. Passer ChoirId ajouterait un contrôle d'appartenance
  // ACTIVE à la chorale, qui répond 403 — donc un toast d'erreur global — pour un membre
  // encore au statut Invité, c'est-à-dire précisément l'état d'un choriste qui vient
  // d'activer son compte et arrive sur son espace membre.
  getUpcoming(params: IPaginationQueryParams): Observable<IPaginatedResult<IEvent>> {
    const httpParams = buildPaginationParams(params).set('Upcoming', 'true');
    return this.http.post<IPaginatedResult<IEvent>>(`${EVENTS_BASE_URL}/GetPaged`, null, { params: httpParams });
  }

  getById(id: string): Observable<IEvent> {
    return this.http.get<IEvent>(`${EVENTS_BASE_URL}/GetById`, { params: { id } });
  }

  create(request: IEvent): Observable<IEvent> {
    return this.http.post<IEvent>(`${EVENTS_BASE_URL}/Create`, request);
  }

  update(id: string, request: IEvent): Observable<IEvent> {
    return this.http.put<IEvent>(`${EVENTS_BASE_URL}/Update`, request, { params: { id } });
  }

  delete(id: string): Observable<unknown> {
    return this.http.delete<unknown>(`${EVENTS_BASE_URL}/Delete`, { params: { id } });
  }

  // Seule route qui fait évoluer Statut (Create/Update l'ignorent). Transitions autorisées
  // côté back : Draft->Publie, Draft->Archive, Publie->Annule, Publie->Archive,
  // Annule->Archive — non revalidées ici, le back est la seule source de vérité (400 si
  // transition invalide ou si Lieu vide au moment de publish).
  changeStatus(id: string, status: EventStatusEnum): Observable<IEvent> {
    const params = new HttpParams().set('id', id).set('status', status.toString());
    return this.http.post<IEvent>(`${EVENTS_BASE_URL}/ChangeStatus`, null, { params });
  }
}
