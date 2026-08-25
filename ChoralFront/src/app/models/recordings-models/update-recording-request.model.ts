// Le contrat back ne détaille pas les champs acceptés par RecordingController.Update
// (seulement "uniquement si Statut=Draft") — surface volontairement réduite aux champs
// descriptifs non structurants (principe du moindre privilège, par analogie avec
// IUpdateScoreRequest qui exclut explicitement Type/SongId/Fichier). À élargir si le
// contrat back est précisé.
export interface IUpdateRecordingRequest {
  ContentOwner: string;
  DownloadAllowed: boolean;
}
