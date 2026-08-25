import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { IAdminDashboardKpi } from '@models/admin-models/admin-dashboard-kpi.model';
import { IPurgeCandidates, IPurgeGuestsResult } from '@models/admin-models/admin-guest-purge.model';

const ADMIN_DASHBOARD_BASE_URL = `${environment.apiUrl}admin-dashboard`;
const ADMIN_GUEST_ACCOUNTS_BASE_URL = `${environment.apiUrl}admin-guest-accounts`;

// Tableau de bord de l'administration générale (`[Authorize(Roles = "Admin")]` côté back, lot
// 5) — lecture seule pour le KPI. La purge RGPD des comptes invités inactives (GetPurgeCandidates
// / PurgeInactive) est rattachée ici plutôt qu'à un 3e fichier de service : le bloc de purge vit
// dans dashboard.component.ts (voir commentaire de placement dans ce composant), et ces deux
// routes appartiennent côté back à AdminGuestAccountsController, pas à AdminDashboardController
// — la base URL diffère donc de GetKpi.
@Injectable({ providedIn: 'root' })
export class AdminDashboardService {
  private readonly http = inject(HttpClient);

  getKpi(): Observable<IAdminDashboardKpi> {
    return this.http.get<IAdminDashboardKpi>(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`);
  }

  // Aperçu, ne purge rien : GET pur, jamais derrière appSubmitOnce (réservé aux actions à effet
  // de bord).
  getPurgeCandidates(): Observable<IPurgeCandidates> {
    return this.http.get<IPurgeCandidates>(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`);
  }

  // Le nombre réellement purgé (AnonymizedCount) peut différer du Count annoncé par l'aperçu —
  // voir IPurgeGuestsResult. Ne jamais afficher le Count de l'aperçu après cet appel.
  purgeInactive(): Observable<IPurgeGuestsResult> {
    return this.http.post<IPurgeGuestsResult>(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/PurgeInactive`, null);
  }
}
