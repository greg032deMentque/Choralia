import { resolveZone } from '@core/zone-resolver';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { RoutePaths } from '@core/route-paths';

function space(partial: Partial<ISpaceRoleAssignment> & Pick<ISpaceRoleAssignment, 'SpaceId' | 'Roles'>): ISpaceRoleAssignment {
  return {
    Name: 'Espace',
    SpaceType: SpaceTypeEnum.Choir,
    ClientId: null,
    ChoirId: null,
    PrimaryVoicePart: null,
    ...partial
  };
}

function user(partial: Partial<IAuthenticatedUser>): IAuthenticatedUser {
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

describe('resolveZone', () => {
  it('claim Admin -> zone admin, quels que soient les autres rattachements', () => {
    const zone = resolveZone(user({ Roles: ['Admin'], SpaceRoles: [space({ SpaceId: 'e1', Roles: ['Singer'] })] }), null);

    expect(zone.kind).toBe('admin');
    expect(zone.path).toBe(`/${RoutePaths.Admin}/${RoutePaths.AdminDashboard}`);
  });

  it('ResponsableClient sans espace de gestion -> zone client', () => {
    const zone = resolveZone(
      user({ ClientRoles: [{ ClientId: 'c1', Name: 'Structure', Roles: ['ClientManager'] }] }),
      null
    );

    expect(zone.kind).toBe('client');
    expect(zone.clientId).toBe('c1');
    expect(zone.path).toBe(`/${RoutePaths.Client}/c1`);
  });

  it('au moins un rôle de gestion sur un espace -> zone gestion sur cet espace', () => {
    const zone = resolveZone(user({ SpaceRoles: [space({ SpaceId: 'e1', Roles: ['Manager'] })] }), null);

    expect(zone.kind).toBe('management');
    expect(zone.spaceId).toBe('e1');
    expect(zone.path).toBe(`/${RoutePaths.Management}/e1/${RoutePaths.Dashboard}`);
  });

  it('appartenance simple uniquement (aucun rôle de gestion) -> zone membre (/moi)', () => {
    const zone = resolveZone(user({ SpaceRoles: [space({ SpaceId: 'e1', Roles: ['Singer'] })] }), null);

    expect(zone.kind).toBe('member');
    expect(zone.path).toBe(`/${RoutePaths.Me}`);
  });

  it('aucun rattachement (ni Admin, ni EspaceRoles, ni ClientRoles) -> /start, jamais une page blanche ni une boucle de 403', () => {
    const zone = resolveZone(user({}), null);

    expect(zone.kind).toBe('no-space');
    expect(zone.path).toBe(`/${RoutePaths.Start}`);
  });

  it('utilisateur non chargé (null) -> écran dédié (même comportement que 0 rattachement)', () => {
    const zone = resolveZone(null, null);

    expect(zone.kind).toBe('no-space');
  });

  it('Admin ET membre simple -> /admin l\'emporte', () => {
    const zone = resolveZone(
      user({ Roles: ['Admin'], SpaceRoles: [space({ SpaceId: 'e1', Roles: ['Singer'] })] }),
      null
    );

    expect(zone.kind).toBe('admin');
  });

  it("ResponsableClient qui est AUSSI Responsable d'une chorale -> /gestion l'emporte sur /client", () => {
    const zone = resolveZone(
      user({
        ClientRoles: [{ ClientId: 'c1', Name: 'Structure', Roles: ['ClientManager'] }],
        SpaceRoles: [space({ SpaceId: 'e1', Roles: ['Manager'] })]
      }),
      null
    );

    expect(zone.kind).toBe('management');
    expect(zone.spaceId).toBe('e1');
  });

  it('espace stocké en session qui n\'existe plus dans EspaceRoles -> repli sur le premier disponible, jamais un espace fantôme', () => {
    const zone = resolveZone(
      user({
        SpaceRoles: [
          space({ SpaceId: 'e1', Roles: ['Manager'] }),
          space({ SpaceId: 'e2', Roles: ['SectionLeader'] })
        ]
      }),
      'espace-fantome-inexistant'
    );

    expect(zone.kind).toBe('management');
    expect(zone.spaceId).toBe('e1');
  });

  it('espace stocké valide et de gestion -> repris tel quel plutôt que le premier disponible', () => {
    const zone = resolveZone(
      user({
        SpaceRoles: [
          space({ SpaceId: 'e1', Roles: ['Manager'] }),
          space({ SpaceId: 'e2', Roles: ['SectionLeader'] })
        ]
      }),
      'e2'
    );

    expect(zone.kind).toBe('management');
    expect(zone.spaceId).toBe('e2');
  });
});
