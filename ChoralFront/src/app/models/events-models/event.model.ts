import { EventTypeEnum } from '@app/enums/event-type.enum';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventEffectiveStateEnum } from '@app/enums/event-effective-state.enum';

// Reflète EventViewModel (back). Statut est piloté exclusivement par
// POST /api/events/ChangeStatus (jamais modifiable via Create/Update — le back l'ignore
// si transmis par ces routes). EffectiveState est calculé côté serveur (Statut + dates),
// toujours en lecture seule côté front.
//
// ChoirId est nul pour un événement autonome (`10-D23`) — corrigé ici (était déclaré
// non-nullable, écart de contrat signalé au lot 6 puis corrigé en correction ciblée) : tout
// affichage qui suppose une chorale porteuse doit prévoir un repli explicite, jamais lire ce
// champ comme s'il était garanti. ClientId porte le client de rattachement d'un événement
// autonome (requis quand ChoirId est absent côté back, ignoré sinon) — un événement rattaché
// à une chorale hérite du client de cette chorale et ne le porte pas lui-même.
export interface IEvent {
  Id: string | null;
  Title: string;
  Description: string | null;
  StartDate: string;
  EndDate: string | null;
  Type: EventTypeEnum;
  Location: string;
  Status: EventStatusEnum;
  EffectiveState: EventEffectiveStateEnum;
  ChoirId: string | null;
  ClientId: string | null;
  ClosedAt: string | null;
}
