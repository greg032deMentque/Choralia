import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';

// Reflète DemandeAdhesionListItemViewModel (back). Vue Responsable de l'espace : file de
// demandes à traiter (POST /api/spaces/{spaceId}/MembershipRequests/GetPaged). DeclineReason est interne,
// jamais transmis au demandeur (voir IMyRequest) — n'afficher ce champ qu'à un Responsable.
export interface IMembershipRequestListItem {
  Id: string;
  SpaceId: string;
  UserId: string;
  UserFullName: string;
  UserEmail: string | null;
  Status: MembershipRequestStatusEnum;
  Message: string | null;
  DeclineReason: string | null;
  CreatedAt: string;
  HandledAt: string | null;
}
