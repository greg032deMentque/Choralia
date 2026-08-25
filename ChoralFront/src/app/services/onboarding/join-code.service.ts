import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import { IJoinCode } from '@models/onboarding-models/join-code.model';

const SPACES_BASE_URL = `${environment.apiUrl}spaces`;

// Management du code de rattachement d'un espace (Responsable uniquement, policy back
// SpaceManager). GenerateOrRotate fait tourner le code immédiatement — le composant
// appelant doit passer par ConfirmService avant tout appel générer()/désactiver() (décision
// produit : la rotation tue l'ancien code sans préavis).
@Injectable({ providedIn: 'root' })
export class JoinCodeService {
  private readonly http = inject(HttpClient);

  getActive(spaceId: string): Observable<IJoinCode> {
    return this.http.get<IJoinCode>(`${SPACES_BASE_URL}/${spaceId}/JoinCode`);
  }

  generateOuRotator(spaceId: string, durationDays?: number): Observable<IJoinCode> {
    let params = new HttpParams();
    if (durationDays !== undefined) {
      params = params.set('durationDays', durationDays.toString());
    }
    return this.http.post<IJoinCode>(`${SPACES_BASE_URL}/${spaceId}/JoinCode`, null, { params });
  }

  desactiver(spaceId: string): Observable<unknown> {
    return this.http.delete<unknown>(`${SPACES_BASE_URL}/${spaceId}/JoinCode`);
  }
}
