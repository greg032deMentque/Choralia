import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète ChoirViewModel (back, POST /api/choirs/Create — policy AdminOrClientManager).
// Status est ignoré par le back à la création (une chorale naît toujours Publiée, `10-D23`) :
// transmis en lecture, jamais envoyé par ICreateChoir ci-dessous.
export interface IChoir {
  Id: string | null;
  ClientId: string;
  Name: string;
  Description: string | null;
  ImageUrl: string | null;
  Status: ChoirStatusEnum;
  ChoirMasterEmail: string | null;
}

// Payload de création. ChoirMasterEmail n'est pas [Required] côté back (le DTO sert aussi de
// corps à Update, qui ne le lit jamais) mais est exigé en pratique par ChoirService.CreateAsync
// — rendu obligatoire ici pour donner un message de validation immédiat plutôt qu'un 400.
export interface ICreateChoir {
  ClientId: string;
  Name: string;
  Description?: string | null;
  ChoirMasterEmail: string;
}

// Reflète AssignChoirMasterViewModel (back, PUT /api/choirs/{choirId}/ChoirMasters/Assign).
// Forme identique à IAssignManagerClient (client-actions.model.ts), volontairement non
// réutilisée : deux DTO back distincts sur deux domaines distincts (chef de chœur d'une
// chorale vs responsable d'une structure) — la convention du projet est un DTO front par DTO
// back, même quand les formes se recoupent.
export interface IAssignChoirMaster {
  Email: string;
}
