import { resolveDisplayedZone, displayedZoneLabel } from '@core/displayed-zone';
import { RoutePaths } from '@core/route-paths';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';

const SPACE_ID = '019fba3a-f3be-7197-accc-9b75f1e63505';
const CLIENT_ID = '019fba3a-f3be-7197-accc-9b75f1e63506';

describe('resolveDisplayedZone', () => {
  it('/admin/... -> zone admin, quel que soit le sous-segment', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Admin}/${RoutePaths.AdminAudit}`)).toEqual({ kind: 'admin' });
  });

  it('/client/:clientId avec un id UUID -> zone client portant cet id', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Client}/${CLIENT_ID}`)).toEqual({ kind: 'client', clientId: CLIENT_ID });
  });

  it('/client/:clientId avec un id non-UUID -> no-space (jamais une zone construite sur un id non validé)', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Client}/pas-un-uuid`)).toEqual({ kind: 'no-space' });
  });

  it('/management/:spaceId/... avec un id UUID -> zone management portant cet id, quel que soit le segment final', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Management}/${SPACE_ID}/${RoutePaths.Dashboard}`)).toEqual({
      kind: 'management',
      spaceId: SPACE_ID
    });
  });

  it('/management/:spaceId avec un id non-UUID -> no-space', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Management}/pas-un-uuid`)).toEqual({ kind: 'no-space' });
  });

  it('/me -> zone member (aucun spaceId : /me ne porte pas de segment dynamique)', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Me}`)).toEqual({ kind: 'member' });
  });

  it('segment racine inconnu, ou URL vide -> no-space', () => {
    expect(resolveDisplayedZone('/inconnu')).toEqual({ kind: 'no-space' });
    expect(resolveDisplayedZone('/')).toEqual({ kind: 'no-space' });
    expect(resolveDisplayedZone('')).toEqual({ kind: 'no-space' });
  });

  it('ignore les query params pour la résolution du segment', () => {
    expect(resolveDisplayedZone(`/${RoutePaths.Management}/${SPACE_ID}?tab=members`)).toEqual({
      kind: 'management',
      spaceId: SPACE_ID
    });
  });
});

describe('displayedZoneLabel', () => {
  const spaceRoles: ISpaceRoleAssignment[] = [
    { SpaceId: SPACE_ID, Name: 'Chorale A', SpaceType: SpaceTypeEnum.Choir, Roles: ['Manager'], ClientId: null, ChoirId: null, PrimaryVoicePart: null }
  ];
  const clientRoles: IClientRoleAssignment[] = [{ ClientId: CLIENT_ID, Name: 'Structure X', Roles: ['ClientManager'] }];

  it('zone management -> nom de l\'espace correspondant dans spaceRoles', () => {
    expect(displayedZoneLabel({ kind: 'management', spaceId: SPACE_ID }, spaceRoles, clientRoles)).toBe('Chorale A');
  });

  it('zone client -> nom de la structure correspondante dans clientRoles', () => {
    expect(displayedZoneLabel({ kind: 'client', clientId: CLIENT_ID }, spaceRoles, clientRoles)).toBe('Structure X');
  });

  it('espace/structure introuvable dans la liste -> chaîne vide, jamais une exception', () => {
    expect(displayedZoneLabel({ kind: 'management', spaceId: 'inconnu' }, spaceRoles, clientRoles)).toBe('');
    expect(displayedZoneLabel({ kind: 'client', clientId: 'inconnu' }, spaceRoles, clientRoles)).toBe('');
  });

  it('zone admin ou membre -> chaîne vide (pas de libellé dédié, comportement inchangé)', () => {
    expect(displayedZoneLabel({ kind: 'admin' }, spaceRoles, clientRoles)).toBe('');
    expect(displayedZoneLabel({ kind: 'member' }, spaceRoles, clientRoles)).toBe('');
  });

  it('zone no-space -> chaîne vide', () => {
    expect(displayedZoneLabel({ kind: 'no-space' }, spaceRoles, clientRoles)).toBe('');
  });
});
