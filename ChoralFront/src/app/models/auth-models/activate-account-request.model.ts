// Reflète ActivateAccountViewModel (back). Corps de POST /api/auth/ActivateAccount — route
// anonyme, réponse 204, et un unique message d'erreur 400 quelle que soit la cause (jeton
// illisible, expiré, déjà consommé, utilisateur inconnu) : l'endpoint est volontairement
// muet pour ne pas devenir un oracle d'existence de compte.
//
// Structurellement identique à IResetPasswordRequest, volontairement NON mutualisé : ce sont
// deux contrats back distincts (ActivateAccountViewModel / ResetPasswordRequestViewModel), et
// un seul type partagé propagerait silencieusement au second toute évolution du premier.
export interface IActivateAccountRequest {
  UserId: string;
  Token: string;
  NewPassword: string;
}
