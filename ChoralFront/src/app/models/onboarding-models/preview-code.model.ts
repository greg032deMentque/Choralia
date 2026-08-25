import { SpaceTypeEnum } from '@app/enums/space-type.enum';

// Reflète PreviewCodeViewModel (back). Réponse de GET /api/onboarding/PreviewCode?code=.
// Uniquement le nom et le type de l'espace — surtout pas le nombre de membres (décision
// produit, donnée exposée à un porteur de code non encore admis). N'afficher rien d'autre
// que ces deux champs, ne rien inventer côté front.
export interface IPreviewCode {
  Name: string;
  SpaceType: SpaceTypeEnum;
}
