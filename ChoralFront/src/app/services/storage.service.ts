import { Injectable } from '@angular/core';

// Wrapper obligatoire — jamais d'accès direct à sessionStorage/localStorage ailleurs
// dans l'app (composants, stores, intercepteurs). Token en sessionStorage uniquement
// (OWASP A02) : effacé à la fermeture de l'onglet/navigateur, y compris avec
// "remember me" côté UI (le flag n'a aucun effet sur le mécanisme de stockage).
const TOKEN_KEY = 'chorale_access_token';
const REFRESH_TOKEN_KEY = 'chorale_refresh_token';
const DEVICE_ID_KEY = 'chorale_device_id';
// Renommé depuis chorale_active_chorale_id (lot 4 zones) : la notion de "chorale actif"
// devient "espace actif", un espace pouvant être une chorale ou un événement.
const ACTIVE_SPACE_ID_KEY = 'chorale_active_espace_id';

@Injectable({ providedIn: 'root' })
export class StorageService {
  GetToken(): string | null {
    return sessionStorage.getItem(TOKEN_KEY);
  }

  SetToken(token: string | null): void {
    if (token) {
      sessionStorage.setItem(TOKEN_KEY, token);
    } else {
      sessionStorage.removeItem(TOKEN_KEY);
    }
  }

  GetRefreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_TOKEN_KEY);
  }

  SetRefreshToken(refreshToken: string | null): void {
    if (refreshToken) {
      sessionStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
    } else {
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    }
  }

  GetDeviceId(): string | null {
    return sessionStorage.getItem(DEVICE_ID_KEY);
  }

  SetDeviceId(deviceId: string | null): void {
    if (deviceId) {
      sessionStorage.setItem(DEVICE_ID_KEY, deviceId);
    } else {
      sessionStorage.removeItem(DEVICE_ID_KEY);
    }
  }

  GetActiveSpaceId(): string | null {
    return sessionStorage.getItem(ACTIVE_SPACE_ID_KEY);
  }

  SetActiveSpaceId(spaceId: string | null): void {
    if (spaceId) {
      sessionStorage.setItem(ACTIVE_SPACE_ID_KEY, spaceId);
    } else {
      sessionStorage.removeItem(ACTIVE_SPACE_ID_KEY);
    }
  }

  Clear(): void {
    sessionStorage.removeItem(TOKEN_KEY);
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    sessionStorage.removeItem(DEVICE_ID_KEY);
    sessionStorage.removeItem(ACTIVE_SPACE_ID_KEY);
  }
}
