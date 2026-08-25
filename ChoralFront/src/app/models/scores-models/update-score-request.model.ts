// ScoreController.Update n'accepte que ces deux champs, et uniquement si la partition
// est au Statut Draft (contrat back) — pas de ré-upload de fichier via cette route.
export interface IUpdateScoreRequest {
  Version: string;
  DownloadAllowed: boolean;
}
