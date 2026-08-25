// Reflète ResendVerificationViewModel (back). Corps de POST /api/auth/ResendVerification.
// Réponse serveur : 204 toujours (invariant anti-énumération), quel que soit l'état du compte.
export interface IResendVerificationRequest {
  Email: string;
}
