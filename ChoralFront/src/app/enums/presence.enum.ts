// Reflète AttendanceEnum (back, Chorale.Common.Enums). Réponse d'un membre à une convocation
// d'événement. Ordinal aligné sur le back, ne pas réordonner.
export enum AttendanceEnum {
  NoReply = 0,
  Attending = 1,
  Maybe = 2,
  NotAttending = 3
}

export function getPresenceLabel(presence: AttendanceEnum): string {
  switch (presence) {
    case AttendanceEnum.NoReply:
      return 'Sans réponse';
    case AttendanceEnum.Attending:
      return 'Présent';
    case AttendanceEnum.Maybe:
      return 'Peut-être';
    case AttendanceEnum.NotAttending:
      return 'Absent';
  }
}
