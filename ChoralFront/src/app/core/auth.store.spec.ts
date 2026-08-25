import { TestBed } from '@angular/core/testing';
import { AuthStore } from '@core/auth.store';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

function buildUser(partial: Partial<IAuthenticatedUser>): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [],
    ClientRoles: [],
    ...partial
  };
}

describe('AuthStore', () => {
  let store: AuthStore;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(AuthStore);
  });

  // currentZone() est la cible par défaut / le repli de zone-resolver.ts (redirection
  // post-connexion, repli des guards quand l'accès demandé est refusé) — PAS la zone AFFICHÉE
  // dans la sidebar/topbar/en-tête X-Space-Id, qui dérive désormais de l'URL couramment
  // rendue (voir core/displayed-zone.ts et DisplayedZoneStore). Ce test valide uniquement le
  // recalcul de currentZone() au changement d'espace actif ; assertions inchangées, le calcul
  // resolveZone() lui-même n'est pas modifié par ce lot.
  it("bascule d'espace actif : activeEspaceRoles et currentZone (cible par défaut) sont recalculés", () => {
    store.setCurrentUser(
      buildUser({
        SpaceRoles: [
          { SpaceId: 'e1', Name: 'Chorale A', SpaceType: SpaceTypeEnum.Choir, Roles: ['Manager'], ClientId: null, ChoirId: null, PrimaryVoicePart: null },
          { SpaceId: 'e2', Name: 'Chorale B', SpaceType: SpaceTypeEnum.Choir, Roles: ['Singer'], ClientId: null, ChoirId: null, PrimaryVoicePart: null }
        ]
      })
    );

    expect(store.activeSpaceId()).toBe('e1');
    expect(store.activeSpaceRoles()).toEqual([UserRoleEnum.Manager]);
    expect(store.currentZone().kind).toBe('management');
    expect(store.currentZone().spaceId).toBe('e1');

    store.setActiveSpace('e2');

    expect(store.activeSpaceId()).toBe('e2');
    expect(store.activeSpaceRoles()).toEqual([UserRoleEnum.Singer]);
    // e2 n'a qu'un rôle d'appartenance simple : currentZone reste dérivée de l'ENSEMBLE des
    // SpaceRoles (e1 reste un espace de management), donc la zone reste 'management' sur e1 — c'est
    // currentZone qui choisit l'espace de management, pas activeSpaceId qui pilote la zone.
    expect(store.currentZone().kind).toBe('management');
    expect(store.currentZone().spaceId).toBe('e1');
  });

  it("bascule vers un espace d'un type différent : activeEspaceType suit l'espace actif", () => {
    store.setCurrentUser(
      buildUser({
        SpaceRoles: [
          { SpaceId: 'e1', Name: 'Choir A', SpaceType: SpaceTypeEnum.Choir, Roles: ['Manager'], ClientId: null, ChoirId: null, PrimaryVoicePart: null },
          {
            SpaceId: 'e2',
            Name: 'Concert de Noël',
            SpaceType: SpaceTypeEnum.Event,
            Roles: ['Organizer'],
            ClientId: null,
            ChoirId: 'e1',
            PrimaryVoicePart: null
          }
        ]
      })
    );

    expect(store.activeSpaceType()).toBe(SpaceTypeEnum.Choir);

    store.setActiveSpace('e2');

    expect(store.activeSpaceType()).toBe(SpaceTypeEnum.Event);
    expect(store.activeSpaceRoles()).toEqual([UserRoleEnum.Organizer]);
  });

  it('0 rattachement (SpaceRoles et ClientRoles vides, pas Admin) : aucun espace actif posé, currentZone = no-space', () => {
    store.setCurrentUser(buildUser({}));

    expect(store.activeSpaceId()).toBeNull();
    expect(store.currentZone().kind).toBe('no-space');
  });
});
