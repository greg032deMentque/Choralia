// Reflète ClientRoleAssignmentViewModel (back, GET /api/auth/Me) — toujours un tableau
// (jamais null côté back). Porte le rattachement ClientManager à une structure (zone
// "Ma structure", /client/:clientId).
//
// Le champ du nom s'appelle `Name`, pas `ClientName` : vérifié sur une réponse réelle de
// l'API (2026-08-01), avec un ClientManager issu du jeu de démonstration. La forme avait été
// *inférée* par symétrie tant qu'aucun jeu de données ne contenait de ClientManager, et
// l'inférence était fausse — aucun test ne pouvait le détecter, le front validant sa propre
// hypothèse. Ne pas redéduire cette forme : la relire dans
// ChoraleBack/Chorale.ViewModels/Auth/ClientRoleAssignmentViewModel.cs.
//
// Attention, les DTO d'authentification ne sont pas homogènes côté back :
// SpaceRoleAssignmentViewModel et ClientRoleAssignmentViewModel exposent `Name`, tandis que
// ChoirRoleAssignmentViewModel (déprécié) expose `ChoirName`.
export interface IClientRoleAssignment {
  ClientId: string;
  Name: string;
  Roles: string[];
}
