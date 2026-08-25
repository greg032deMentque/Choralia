// Formatage lisible des plafonds/consommations en octets (Go/Mo) — jamais de nombre brut à
// l'écran (`08` § Clients, `10-D23`). Base 1024, une décimale, séparateur français.
//
// 'fr-FR' reste écrit en dur volontairement, même depuis que LOCALE_ID='fr-FR' est fourni
// dans app.config.ts (correction du format de date US) : cette fonction est une utilitaire
// pure appelée aussi bien depuis des templates (`{{ formatBytes(x) }}`) que depuis du code
// de composant en dehors de tout contexte d'injection Angular — contrairement à DecimalPipe,
// elle ne peut pas lire LOCALE_ID via inject() sans devenir elle-même un pipe/service injecté,
// ce qui casserait ses appels directs actuels. Les deux valeurs coïncident et le resteront
// tant que l'application n'a qu'une seule locale : ce n'est pas une duplication accidentelle.
const UNITS = ['o', 'Ko', 'Mo', 'Go', 'To'] as const;

const NUMBER_FORMAT = new Intl.NumberFormat('fr-FR', { minimumFractionDigits: 0, maximumFractionDigits: 1 });

export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes < 0) return '—';
  if (bytes === 0) return '0 o';

  let value = bytes;
  let unitIndex = 0;
  while (value >= 1024 && unitIndex < UNITS.length - 1) {
    value /= 1024;
    unitIndex++;
  }

  return `${NUMBER_FORMAT.format(value)} ${UNITS[unitIndex]}`;
}

// Pourcentage de consommation d'un plafond — jamais de division par zéro : un plafond à 0
// renvoie 0 (pas 100%, qui laisserait croire à une saturation inexistante). Volontairement non
// plafonné à 100 : un dépassement (consommation > limite, ex. après abaissement d'un plafond)
// doit rester visible tel quel (> 100%), pas masqué par un Math.min.
export function percentageUsage(consumed: number, limit: number): number {
  if (limit <= 0) return 0;
  return Math.round((consumed / limit) * 100);
}

// Seuil de mise en évidence visuelle (`08` § Clients : "au-delà de 80 %").
export const USAGE_ALERT_THRESHOLD_PERCENT = 80;

export function isUsageCritical(consumed: number, limit: number): boolean {
  return percentageUsage(consumed, limit) >= USAGE_ALERT_THRESHOLD_PERCENT;
}
