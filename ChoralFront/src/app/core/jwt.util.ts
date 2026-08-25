// Décodage JWT minimal côté client, à seule fin de lire `exp` pour anticiper le refresh.
// Aucune validation de signature n'est faite ici — le backend reste seul juge de la
// validité réelle du token (OWASP A02 : ne jamais faire confiance à un JWT décodé
// côté client pour une décision de sécurité, uniquement pour l'UX de refresh).
interface IDecodedJwtPayload {
  exp?: number;
  [claim: string]: unknown;
}

function decodeJwtPayload(token: string): IDecodedJwtPayload | null {
  const parts = token.split('.');
  if (parts.length !== 3) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const json = atob(base64);
    return JSON.parse(json) as IDecodedJwtPayload;
  } catch {
    return null;
  }
}

// Marge de sécurité : considère le token expiré 30s avant son expiration réelle pour
// laisser le temps au refresh de s'exécuter avant que le backend ne le rejette.
const EXPIRY_MARGIN_SECONDS = 30;

export function isTokenExpired(token: string | null): boolean {
  if (!token) return true;
  const payload = decodeJwtPayload(token);
  if (!payload?.exp) return true;
  const nowSeconds = Date.now() / 1000;
  return payload.exp - EXPIRY_MARGIN_SECONDS <= nowSeconds;
}
