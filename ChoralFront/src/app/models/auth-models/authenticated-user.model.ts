import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';

// Reflète AuthenticatedUserViewModel (back). AccessToken/RefreshToken existent côté
// contrat mais ne sont jamais utilisés côté front depuis /Me : ils restent null sur
// cette route (voir ChoralFront/CLAUDE.md — Authentification). Seules les réponses de
// Login/RefreshToken peuplent le token, via IToken (token.model.ts).
export interface IAuthenticatedUser {
  Id: string;
  Email: string;
  Firstname: string;
  Lastname: string;
  Roles: string[];
  SpaceRoles: ISpaceRoleAssignment[];
  ClientRoles: IClientRoleAssignment[];
}
