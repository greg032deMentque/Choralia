// Déclenchement d'un téléchargement à partir d'un Blob déjà récupéré par HttpClient. Le
// transport reste HttpClient dans tous les cas : une requête native (<a href> vers l'API,
// <audio src>) n'est pas interceptée par TokenInterceptor et partirait sans jeton.
//
// Point unique de la manipulation ObjectURL + ancre : elle était recopiée à l'identique dans
// ScoreListComponent et RecordingListComponent, et les deux copies portaient le même défaut
// (révocation synchrone, voir ci-dessous).
//
// La révocation est DIFFÉRÉE : appelée juste après link.click(), elle invalide l'URL avant que
// le navigateur n'ait démarré l'écriture du fichier — le téléchargement avorte alors sans
// aucun message. Le délai n'a pas à couvrir la durée du transfert : le navigateur capture le
// Blob au traitement du clic, il suffit de ne pas révoquer dans le même tour de boucle.
const OBJECT_URL_REVOKE_DELAY_MS = 1000;

export function triggerBlobDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';

  // Ancre rattachée au document avant le clic : Firefox ignore click() sur un élément détaché.
  document.body.appendChild(link);
  link.click();
  link.remove();

  setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_REVOKE_DELAY_MS);
}
