import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import { environment } from '@env/environment';
import { appendOptionalArrayParam, appendOptionalParam, buildPaginationParams } from '@core/pagination-params.util';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';
import {
  IAdminChoirUserListItem,
  IAdminChoirUsersFilter
} from '@models/admin-models/admin-choir-user-list-item.model';
import {
  IAdminEventUserListItem,
  IAdminEventUsersFilter
} from '@models/admin-models/admin-event-user-list-item.model';
import { IAdminUserListItem, IAdminUsersFilter } from '@models/admin-models/admin-user-list-item.model';
import { IAdminUnattachedUserListItem } from '@models/admin-models/admin-unattached-user-list-item.model';
import { IAdminUserDetail } from '@models/admin-models/admin-user-detail.model';
import { IAdminUserSetActive, IAdminUserUpdateIdentity, ICreateAdminUser } from '@models/admin-models/admin-user-actions.model';
import { UserRoleEnum, userRoleFromString } from '@app/enums/user-role.enum';

const ADMIN_USERS_BASE_URL = `${environment.apiUrl}admin-users`;

// Formes brutes telles que sérialisées par le back avant conversion des rôles (chaînes de
// claims JWT) en UserRoleEnum — même convention que ChoirMembersService/IMemberChoir.
type IAdminChoirUserListItemApi = Omit<IAdminChoirUserListItem, 'Roles'> & { Roles: string[] };
type IAdminEventUserListItemApi = Omit<IAdminEventUserListItem, 'Role'> & { Role: string };
type IAdminUserDetailApi = Omit<IAdminUserDetail, 'Choirs' | 'Events'> & {
  Choirs: IAdminChoirUserListItemApi[];
  Events: IAdminEventUserListItemApi[];
};

function mapChoirUserItem(raw: IAdminChoirUserListItemApi): IAdminChoirUserListItem {
  return { ...raw, Roles: raw.Roles.map(userRoleFromString).filter((role): role is UserRoleEnum => role !== null) };
}

function mapEventUserItem(raw: IAdminEventUserListItemApi): IAdminEventUserListItem {
  return { ...raw, Role: userRoleFromString(raw.Role) };
}

function mapUserDetail(raw: IAdminUserDetailApi): IAdminUserDetail {
  return {
    ...raw,
    Choirs: raw.Choirs.map(mapChoirUserItem),
    Events: raw.Events.map(mapEventUserItem)
  };
}

// Toutes les routes sont réservées au claim global Admin (back : [Authorize(Roles = "Admin")]
// sur AdminUserController) — pas d'en-tête X-Space-Id sur la zone admin (ne scope pas par
// chorale actif, contrairement aux services de la zone /management).
@Injectable({ providedIn: 'root' })
export class AdminUserService {
  private readonly http = inject(HttpClient);

  // Une ligne = un rattachement membre/chorale, pas une personne (voir modèle). Filtre
  // ChoirIds/Role/Statut/Voix/IsActive tous optionnels, transmis en query string (convention
  // [FromQuery] du back pour ces endpoints POST). ChoirIds : paramètre répété
  // (?ChoirIds=a&ChoirIds=b), voir appendOptionalArrayParam.
  getChoirUsersPaged(
    pagination: IPaginationQueryParams,
    filter: IAdminChoirUsersFilter
  ): Observable<IPaginatedResult<IAdminChoirUserListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalArrayParam(params, 'ChoirIds', filter.ChoirIds);
    params = appendOptionalParam(params, 'Role', filter.Role);
    params = appendOptionalParam(params, 'Status', filter.Status);
    params = appendOptionalParam(params, 'Voix', filter.VoicePart);
    params = appendOptionalParam(params, 'IsActive', filter.IsActive);

    return this.http
      .post<IPaginatedResult<IAdminChoirUserListItemApi>>(`${ADMIN_USERS_BASE_URL}/GetChoirUsersPaged`, null, { params })
      .pipe(map(res => ({ ...res, Items: res.Items.map(mapChoirUserItem) })));
  }

  // Une ligne = un rattachement membre/événement, pas une personne (voir modèle). ChoirName
  // peut être null (événement autonome) — géré au niveau du template consommateur, pas ici.
  // EventIds : paramètre répété (?EventIds=a&EventIds=b), voir appendOptionalArrayParam.
  getEventUsersPaged(
    pagination: IPaginationQueryParams,
    filter: IAdminEventUsersFilter
  ): Observable<IPaginatedResult<IAdminEventUserListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalArrayParam(params, 'EventIds', filter.EventIds);
    params = appendOptionalParam(params, 'Role', filter.Role);
    params = appendOptionalParam(params, 'Presence', filter.Presence);
    params = appendOptionalParam(params, 'Upcoming', filter.Upcoming);

    return this.http
      .post<IPaginatedResult<IAdminEventUserListItemApi>>(`${ADMIN_USERS_BASE_URL}/GetEventUsersPaged`, null, { params })
      .pipe(map(res => ({ ...res, Items: res.Items.map(mapEventUserItem) })));
  }

  // Comptes administrateurs (onglet "Administrateurs") — filtre IsActive/IsGuestAccount
  // optionnel (AdminUsersPagedFilterViewModel, back), en complément de la pagination/texte
  // libre. IsGuestAccount n'a pas de sens métier ici mais le contrat back l'accepte (voir
  // IAdminUsersFilter).
  getPaged(pagination: IPaginationQueryParams, filter: IAdminUsersFilter = {}): Observable<IPaginatedResult<IAdminUserListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'IsActive', filter.IsActive);
    params = appendOptionalParam(params, 'IsGuestAccount', filter.IsGuestAccount);

    return this.http.post<IPaginatedResult<IAdminUserListItem>>(`${ADMIN_USERS_BASE_URL}/GetPaged`, null, { params });
  }

  // Comptes sans aucun rattachement (onglet "Sans rattachement") — inclut les ResponsableClient
  // sans espace, invisibles partout ailleurs dans la zone admin. Même filtre
  // (AdminUsersPagedFilterViewModel) que GetPaged.
  getUnattachedUsersPaged(
    pagination: IPaginationQueryParams,
    filter: IAdminUsersFilter = {}
  ): Observable<IPaginatedResult<IAdminUnattachedUserListItem>> {
    let params = buildPaginationParams(pagination);
    params = appendOptionalParam(params, 'IsActive', filter.IsActive);
    params = appendOptionalParam(params, 'IsGuestAccount', filter.IsGuestAccount);

    return this.http.post<IPaginatedResult<IAdminUnattachedUserListItem>>(
      `${ADMIN_USERS_BASE_URL}/GetUnattachedUsersPaged`,
      null,
      { params }
    );
  }

  // Fiche agrégée : déduplique tous les rattachements d'une personne. userId (pas id de
  // rattachement) — c'est l'identifiant transmis par les colonnes UserId (onglets
  // Chorales/Événements) ou Id (onglets Administrateurs/Sans rattachement, déjà un userId).
  getUserDetail(userId: string): Observable<IAdminUserDetail> {
    return this.http
      .get<IAdminUserDetailApi>(`${ADMIN_USERS_BASE_URL}/GetUserDetail`, { params: { userId } })
      .pipe(map(mapUserDetail));
  }

  // 409 si l'email est déjà pris par un autre compte — rien n'est modifié côté back dans ce
  // cas, l'erreur remonte telle quelle (voir admin-user.service.spec.ts) pour que la fiche
  // affiche un message inline exploitable.
  updateIdentity(payload: IAdminUserUpdateIdentity): Observable<IAdminUserDetail> {
    return this.http.put<IAdminUserDetailApi>(`${ADMIN_USERS_BASE_URL}/UpdateIdentity`, payload).pipe(map(mapUserDetail));
  }

  // 403 si l'admin tente de se désactiver lui-même (vérifié côté back, pas revalidé ici).
  setActive(payload: IAdminUserSetActive): Observable<IAdminUserDetail> {
    return this.http.put<IAdminUserDetailApi>(`${ADMIN_USERS_BASE_URL}/SetActive`, payload).pipe(map(mapUserDetail));
  }

  // Réponses 204 No Contenu — Observable<unknown> plutôt que <void> (interdit par ESLint
  // comme argument générique, voir EventService.delete pour la même convention).
  resetPassword(userId: string): Observable<unknown> {
    return this.http.post<unknown>(`${ADMIN_USERS_BASE_URL}/ResetPassword`, null, { params: { userId } });
  }

  // 409 si le compte n'est pas (ou plus) une invitation en attente.
  resendInvitation(userId: string): Observable<unknown> {
    return this.http.post<unknown>(`${ADMIN_USERS_BASE_URL}/ResendInvitation`, null, { params: { userId } });
  }

  // 403 auto-suppression, 409 dernier administrateur (vérifiés côté back).
  delete(userId: string): Observable<unknown> {
    return this.http.delete<unknown>(`${ADMIN_USERS_BASE_URL}/Delete`, { params: { userId } });
  }

  createAdmin(payload: ICreateAdminUser): Observable<IAdminUserListItem> {
    return this.http.post<IAdminUserListItem>(`${ADMIN_USERS_BASE_URL}/Create`, payload);
  }
}
