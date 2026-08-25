import { EventTypeEnum } from '@app/enums/event-type.enum';

// Reflète CreateEventViewModel (back). Corps de POST /api/onboarding/CreateEvent.
// Structure est facultatif — voir ICreateChoirRequest.Structure. Un événement créé ici est
// autonome (pas rattaché à une chorale) : le back détermine le ClientId depuis Structure/le
// client existant de l'utilisateur.
export interface ICreateEventRequest {
  Title: string;
  Description?: string;
  StartDate: string;
  EndDate?: string;
  Type: EventTypeEnum;
  Location?: string;
  Structure?: string;
}
