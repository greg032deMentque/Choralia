// Durée lisible « m:ss » (« h:mm:ss » au-delà d'une heure) — jamais un nombre de secondes brut
// dans une interface de lecture.
//
// Une valeur non finie rend un tiret plutôt que « NaN:aN » : c'est l'état réel de
// HTMLAudioElement.duration tant que les métadonnées de la piste ne sont pas chargées, donc un
// état traversé à chaque changement de piste, pas un cas théorique.
export function formatDuration(totalSeconds: number): string {
  if (!Number.isFinite(totalSeconds) || totalSeconds < 0) return '--:--';

  const seconds = Math.floor(totalSeconds % 60);
  const minutes = Math.floor(totalSeconds / 60) % 60;
  const hours = Math.floor(totalSeconds / 3600);
  const paddedSeconds = seconds.toString().padStart(2, '0');

  return hours > 0
    ? `${hours}:${minutes.toString().padStart(2, '0')}:${paddedSeconds}`
    : `${minutes}:${paddedSeconds}`;
}
