import { mapRolesFromApi } from './choir-roles.util';
import { UserRoleEnum } from '@app/enums/user-role.enum';

describe('mapRolesFromApi', () => {
  it('convertit les chaînes reconnues en UserRoleEnum, dans l’ordre reçu', () => {
    expect(mapRolesFromApi(['Manager', 'Singer'])).toEqual([UserRoleEnum.Manager, UserRoleEnum.Singer]);
  });

  it('ignore silencieusement une valeur non reconnue plutôt que de faire échouer tout le mapping', () => {
    expect(mapRolesFromApi(['Manager', 'RoleInconnu', 'Singer'])).toEqual([UserRoleEnum.Manager, UserRoleEnum.Singer]);
  });

  it('retourne un tableau vide pour une liste vide', () => {
    expect(mapRolesFromApi([])).toEqual([]);
  });
});
