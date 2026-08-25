import { InstructionStatusEnum } from '@app/enums/status-instruction.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// Reflète InstructionViewModel (back). Une consigne n'a qu'une cible : le chant qui la porte.
// Les portées chorale / pupitre / événement ont été retirées du modèle back (migration
// InstructionsSongScopeOnly) — ne pas réintroduire de champ ChoirId ou EventId ici.
export interface IInstruction {
  Id?: string;
  SongId: string;
  // Nul = consigne adressée à tout le chœur sur ce chant. Renseignée = consigne de pupitre,
  // seul cas ouvert au chef de pupitre, et uniquement sur SA voix (arbitré côté serveur).
  VoicePart: VoicePartEnum | null;
  Title: string | null;
  Content: string;
  Status: InstructionStatusEnum;
  PublishedAt: string | null;
  AuthorUserId: string;
  AuthorName: string | null;
}

// Corps de POST /api/instructions/Create.
export interface ICreateInstructionRequest {
  SongId: string;
  VoicePart?: VoicePartEnum;
  Title?: string;
  Content: string;
}

// Corps de PUT /api/instructions/Update — l'identifiant est dans le CORPS, pas en query param
// (convention propre à ce contrôleur, différente de SongController.Update).
export interface IUpdateInstructionRequest {
  Id: string;
  Title?: string;
  Content: string;
}

// Filtres de POST /api/instructions/GetPaged, en query params. Le back accepte aussi VoicePart
// et Status ; ils ne sont pas déclarés ici tant qu'aucun écran ne les envoie — un champ de
// filtre inutilisé se lit comme une capacité de l'écran qui n'existe pas.
export interface IInstructionListFilters {
  SongId?: string;
}
