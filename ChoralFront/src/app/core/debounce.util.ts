// Anti-rebond générique pour les gestionnaires déclenchés à chaque frappe (ex. filtres texte
// des lists paginées) — évite un appel HTTP par caractère saisi. Utilisé à la place d'un
// Subject RxJS + debounceTime pour rester cohérent avec l'approche Signal API du projet
// (pas de flux RxJS pour du pur UI state).
export function debounce<Args extends unknown[]>(fn: (...args: Args) => void, delayMs: number): (...args: Args) => void {
  let timeoutId: ReturnType<typeof setTimeout> | undefined;
  return (...args: Args): void => {
    if (timeoutId !== undefined) {
      clearTimeout(timeoutId);
    }
    timeoutId = setTimeout(() => fn(...args), delayMs);
  };
}
