import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { IChoirKpi } from '@models/common-models/dashboard-summary.model';

const DASHBOARD_BASE_URL = `${environment.apiUrl}dashboard`;

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  // La chorale est portée par l'en-tête X-Space-Id, posé par l'intercepteur : aucun
  // identifiant en paramètre, sinon la valeur autorisée et la valeur lue pourraient
  // diverger.
  getChoirKpi(): Observable<IChoirKpi> {
    return this.http.get<IChoirKpi>(`${DASHBOARD_BASE_URL}/ChoirKpi`);
  }
}
