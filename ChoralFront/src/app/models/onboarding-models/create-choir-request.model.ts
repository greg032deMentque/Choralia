// Reflète CreateChoirViewModel (back). Corps de POST /api/onboarding/CreateChoir. Structure
// est facultatif : paroisse, école de musique, association. Laissé vide, un Client est créé en
// silence côté back, nommé d'après la chorale — le mot « Client » n'apparaît jamais côté
// utilisateur. Un champ vide ne doit jamais être transmis comme chaîne vide : absent ou null.
export interface ICreateChoirRequest {
  Name: string;
  Description?: string;
  Structure?: string;
}
