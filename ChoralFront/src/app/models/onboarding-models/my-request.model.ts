import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';

// Reflète MyRequestViewModel (back). Vue demandeur : ses propres demandes (GET
// /api/onboarding/MyRequests). Ne porte jamais DeclineReason — décision produit, le motif de
// refus reste interne, le demandeur reçoit un message neutre.
export interface IMyRequest {
  Id: string;
  SpaceId: string;
  SpaceName: string;
  Status: MembershipRequestStatusEnum;
  Message: string | null;
  CreatedAt: string;
}
