import { ClientStatusEnum } from '@app/enums/status-client.enum';

// Reflète CreateClientViewModel (POST /api/clients/Create).
export interface ICreateClient {
  Name: string;
  ContactName?: string | null;
  ContactEmail?: string | null;
}

// Reflète UpdateClientViewModel (PUT /api/clients/Update).
export interface IUpdateClient {
  Id: string;
  Name: string;
  ContactName?: string | null;
  ContactEmail?: string | null;
}

// Reflète UpdateLimitsClientViewModel (PUT /api/clients/UpdateLimits) — réservé à
// l'administration générale, jamais modifiable depuis la zone /client (« Ma structure »).
export interface IUpdateLimitsClient {
  Id: string;
  ChoirLimit: number;
  MemberLimit: number;
  StorageQuotaBytes: number;
  MaxFileSizeBytes: number;
}

// Reflète ChangeStatusClientViewModel (PUT /api/clients/ChangeStatus).
export interface IChangeStatusClient {
  Id: string;
  Status: ClientStatusEnum;
}

// Reflète AssignManagerClientViewModel (POST /api/clients/{clientId}/Responsables) —
// l'utilisateur doit déjà avoir un compte, ce n'est pas un flux d'invitation.
export interface IAssignManagerClient {
  Email: string;
}

// Reflète ImpactSuspensionViewModel (GET /api/clients/{clientId}/SuspensionImpact) —
// conséquences chiffrées présentées avant confirmation d'une suspension.
export interface IImpactSuspensionClient {
  ChoirCount: number;
  MemberCount: number;
}
