// Reflète DeclineRequestViewModel (back). Corps de POST
// /api/spaces/{spaceId}/MembershipRequests/{id}/Decline. DeclineReason est interne : jamais transmis au
// demandeur (décision produit) — champ à consommation strictement interne au Responsable.
export interface IDeclineRequestRequest {
  DeclineReason?: string;
}
