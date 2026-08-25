export enum VoicePartEnum {
  Alto = 0,
  Soprano = 1,
  Bass = 2,
  Tenor = 3
}

// Ordre d'affichage des pupitres dans les listes déroulantes (aigu -> grave). Liste
// canonique, à consommer plutôt qu'à recopier : plusieurs écrans antérieurs en portent
// encore une copie locale, dette signalée à la livraison du lot invitation.
export const ALL_VOICE_PARTS: readonly VoicePartEnum[] = [
  VoicePartEnum.Soprano,
  VoicePartEnum.Alto,
  VoicePartEnum.Tenor,
  VoicePartEnum.Bass
];

export function getVoicePartLabel(voicePart: VoicePartEnum): string {
  switch (voicePart) {
    case VoicePartEnum.Alto:
      return 'Alto';
    case VoicePartEnum.Soprano:
      return 'Soprano';
    case VoicePartEnum.Bass:
      return 'Basse';
    case VoicePartEnum.Tenor:
      return 'Ténor';
  }
}

// Rend une liste de pupitres lisible ("Alto, Soprano") — jamais une concaténation brute des
// libellés unitaires. Même convention que getUserRolesLabel (user-role.enum.ts).
export function getVoicePartsLabel(voiceParts: readonly VoicePartEnum[]): string {
  return voiceParts.map(getVoicePartLabel).join(', ');
}
