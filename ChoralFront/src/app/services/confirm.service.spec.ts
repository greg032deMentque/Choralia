import { ConfirmService } from './confirm.service';

// ConfirmService ne rend plus rien lui-même (voir confirm.service.ts) — le rendu réel est
// délégué à ConfirmModalComponent, monté une seule fois par App et hors du périmètre de ce
// test unitaire (composant purement présentationnel, cf. règle « à ne pas tester » de
// ChoralFront/CLAUDE.md). Ce fichier couvre le seul comportement non trivial du service : le
// couplage requête/résolution du signal `request` avec la Promise retournée par confirm().

describe('ConfirmService', () => {
  let service: ConfirmService;

  beforeEach(() => {
    service = new ConfirmService();
  });

  it('résout true quand resolve(true) est appelé, et vide la requête en attente', async () => {
    const promise = service.confirm({ title: 'Archiver', message: 'Confirmer ?' });
    expect(service.request()).not.toBeNull();

    service.resolve(true);

    await expect(promise).resolves.toBe(true);
    expect(service.request()).toBeNull();
  });

  it('résout false quand resolve(false) est appelé (annulation ou fermeture de la modale)', async () => {
    const promise = service.confirm({ title: 'Archiver', message: 'Confirmer ?' });

    service.resolve(false);

    await expect(promise).resolves.toBe(false);
  });

  it("annule automatiquement à false la confirmation en attente si confirm() est rappelé avant sa résolution", async () => {
    // Cas limite documenté dans le plan de migration : sans cette règle, la première Promise
    // resterait indéfiniment en attente (fuite) puisque son resolver serait silencieusement
    // écrasé par le second appel.
    const firstPromise = service.confirm({ title: 'Première demande', message: 'A' });
    const secondPromise = service.confirm({ title: 'Seconde demande', message: 'B' });

    await expect(firstPromise).resolves.toBe(false);

    expect(service.request()?.title).toBe('Seconde demande');

    service.resolve(true);
    await expect(secondPromise).resolves.toBe(true);
  });

  it("resolve() sans confirmation en attente n'a aucun effet (pas d'exception)", () => {
    expect(() => service.resolve(true)).not.toThrow();
    expect(service.request()).toBeNull();
  });
});
