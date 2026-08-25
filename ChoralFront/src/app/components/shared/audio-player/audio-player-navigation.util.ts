// Logique pure de navigation dans une playlist (ordre séquentiel ou aléatoire, avec ou
// sans boucle) — extraite du composant pour rester testable sans DOM/<audio>.

export function buildSequentialOrder(length: number): number[] {
  return Array.from({ length }, (_, i) => i);
}

// Fisher-Yates. Si currentIndex est fourni et présent dans le résultat, il est replacé en
// position 0 pour ne pas interrompre la lecture en cours au moment de l'activation du mode
// aléatoire.
export function buildShuffledOrder(length: number, currentIndex: number | null = null, random: () => number = Math.random): number[] {
  const indices = buildSequentialOrder(length);
  for (let i = indices.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [indices[i], indices[j]] = [indices[j], indices[i]];
  }
  if (currentIndex !== null) {
    const pos = indices.indexOf(currentIndex);
    if (pos > 0) {
      [indices[0], indices[pos]] = [indices[pos], indices[0]];
    }
  }
  return indices;
}

// Retourne la position suivante dans `order`, ou null si la fin est atteinte sans boucle
// (fin de playlist -> arrêt de la lecture).
export function getNextPosition(position: number, orderLength: number, loop: boolean): number | null {
  if (orderLength === 0) return null;
  const next = position + 1;
  if (next < orderLength) return next;
  return loop ? 0 : null;
}

// Retourne la position précédente dans `order`, ou null si le début est atteint sans
// boucle.
export function getPreviousPosition(position: number, orderLength: number, loop: boolean): number | null {
  if (orderLength === 0) return null;
  const prev = position - 1;
  if (prev >= 0) return prev;
  return loop ? orderLength - 1 : null;
}
