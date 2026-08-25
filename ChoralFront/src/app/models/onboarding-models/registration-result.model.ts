// Reflète RegistrationResultViewModel (back). Réponse UNIQUE de POST /api/auth/Register, quel
// que soit le cas réel (email libre, compte déjà complet, compte invité non revendiqué) —
// décision produit : anti-énumération assumée (11 §3.1). Ne jamais tenter d'en déduire l'issue
// réelle côté front.
export interface IRegistrationResult {
  Message: string;
}
