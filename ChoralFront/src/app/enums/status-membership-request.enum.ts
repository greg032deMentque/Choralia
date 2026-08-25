// Reflète StatutDemandeAdhesionEnum (back, Chorale.Common.Enums). Ordinal aligné sur le back,
// ne pas réordonner ni insérer de valeur au milieu — toute valeur ajoutée côté back se reflète
// en fin d'enum front, avec le même entier des deux côtés.
export enum MembershipRequestStatusEnum {
  // Enregistrée, en attente de traitement par un Responsable de l'espace.
  Pending = 0,
  // Acceptée : le demandeur est devenu membre de l'espace.
  Approved = 1,
  // Refusée par un Responsable. Bloque une nouvelle demande sur le même espace pendant 30 jours.
  Declined = 2,
  // Annulée par le demandeur lui-même avant traitement.
  Cancelled = 3
}

export function getStatusMembershipRequestLabel(status: MembershipRequestStatusEnum): string {
  switch (status) {
    case MembershipRequestStatusEnum.Pending:
      return 'En attente';
    case MembershipRequestStatusEnum.Approved:
      return 'Admise';
    case MembershipRequestStatusEnum.Declined:
      return 'Refusée';
    case MembershipRequestStatusEnum.Cancelled:
      return 'Annulée';
  }
}
