import { ChoirStatusEnum } from '@app/enums/status-choir.enum';

// Reflète ChoirViewModel (back), réponse de POST /api/onboarding/CreateChoir. Modèle dédié
// à l'onboarding (plutôt qu'un IChorale partagé, inexistant côté front à ce jour) pour rester
// strictement dans le périmètre de ce lot — voir écarts assumés du récapitulatif de génération.
export interface IChoirCreationResult {
  Id: string | null;
  ClientId: string;
  Name: string;
  Description: string | null;
  ImageUrl: string | null;
  Status: ChoirStatusEnum;
}
