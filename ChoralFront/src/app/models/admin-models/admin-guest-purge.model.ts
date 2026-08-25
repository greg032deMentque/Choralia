// Reflète PurgeCandidatsViewModel (back, AdminGuestAccountsController.GetPurgeCandidates) —
// aperçu, sans purger, des comptes invités concernés par PurgeInactiveGuestsAsync.
export interface IPurgeCandidatItem {
  UserId: string;
  Email: string | null;
  Firstname: string;
  Lastname: string;
  LastActivityAt: string;
}

export interface IPurgeCandidates {
  Count: number;
  // true si d'autres candidats existent au-delà du lot chargé (PurgeBatchSize, back).
  HasMore: boolean;
  Candidates: IPurgeCandidatItem[];
}

// Reflète PurgeGuestsResultViewModel (back, AdminGuestAccountsController.PurgeInactive). Le
// nombre réellement purgé (AnonymizedCount) peut différer du Count annoncé par l'aperçu — un
// compte revendiqué (rattaché à un espace) entre l'aperçu et l'exécution n'est pas purgé.
// Toujours afficher AnonymizedCount après l'action, jamais le Count de l'aperçu.
export interface IPurgeGuestsResult {
  AnonymizedCount: number;
  HasMore: boolean;
}
