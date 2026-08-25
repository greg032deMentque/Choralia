import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète AdminChoraleUpdateViewModel — l'administration générale n'écrit que sur les
// informations d'une chorale (Nom/Description), jamais sur son contenu ni son ClientId.
export interface IAdminChoirUpdate {
  Id: string;
  Name: string;
  Description?: string | null;
}

// Reflète ChangeStatusChoirViewModel (PUT /api/admin-choirs/ChangeStatus).
export interface IAdminChoirChangeStatus {
  Id: string;
  Status: ChoirStatusEnum;
}

// Reflète AdminChoraleImpactViewModel (GET .../ImpactArchivage) — conséquences chiffrées
// présentées avant confirmation d'un passage à Archive.
export interface IAdminChoirImpact {
  MemberCount: number;
  SongCount: number;
  EventCount: number;
}
