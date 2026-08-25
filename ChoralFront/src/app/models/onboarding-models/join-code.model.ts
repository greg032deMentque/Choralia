// Reflète JoinCodeViewModel (back). Réponse de GET/POST/DELETE
// /api/spaces/{spaceId}/JoinCode. Code et ExpiresAt sont nuls quand aucun code n'a
// jamais été généré pour cet espace.
export interface IJoinCode {
  Code: string | null;
  ExpiresAt: string | null;
  IsActive: boolean;
}
