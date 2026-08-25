import { buildSequentialOrder, buildShuffledOrder, getNextPosition, getPreviousPosition } from './audio-player-navigation.util';

// Logique métier non triviale du lecteur audio (boucle + aléatoire) — la seule partie de
// AudioPlayerComponent qui mérite un test (règle projet ChoralFront : pas de test pour le
// binding de template pur).
describe('audio-player-navigation.util', () => {
  it('buildSequentialOrder retourne les indices dans l\'ordre', () => {
    expect(buildSequentialOrder(4)).toEqual([0, 1, 2, 3]);
    expect(buildSequentialOrder(0)).toEqual([]);
  });

  it('buildShuffledOrder retourne une permutation complète des indices', () => {
    const order = buildShuffledOrder(5, null, () => 0.999);
    expect(order).toHaveLength(5);
    expect([...order].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4]);
  });

  it('buildShuffledOrder replace la piste en cours en position 0 pour ne pas interrompre la lecture', () => {
    // random déterministe : produit un ordre où currentIndex (2) n'est pas déjà en tête,
    // on vérifie qu'il est bien repositionné en position 0 après coup.
    const order = buildShuffledOrder(5, 2, () => 0.1);
    expect(order[0]).toBe(2);
    expect([...order].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4]);
  });

  it('getNextPosition avance normalement dans la playlist', () => {
    expect(getNextPosition(0, 3, false)).toBe(1);
    expect(getNextPosition(1, 3, false)).toBe(2);
  });

  it('getNextPosition retourne null en fin de playlist sans boucle', () => {
    expect(getNextPosition(2, 3, false)).toBeNull();
  });

  it('getNextPosition revient au début en fin de playlist avec boucle', () => {
    expect(getNextPosition(2, 3, true)).toBe(0);
  });

  it('getNextPosition retourne null sur une playlist vide', () => {
    expect(getNextPosition(0, 0, true)).toBeNull();
  });

  it('getPreviousPosition recule normalement dans la playlist', () => {
    expect(getPreviousPosition(2, 3, false)).toBe(1);
  });

  it('getPreviousPosition retourne null au début de playlist sans boucle', () => {
    expect(getPreviousPosition(0, 3, false)).toBeNull();
  });

  it('getPreviousPosition revient à la fin au début de playlist avec boucle', () => {
    expect(getPreviousPosition(0, 3, true)).toBe(2);
  });
});
