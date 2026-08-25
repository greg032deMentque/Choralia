// Validation des paramètres de route avant tout appel HTTP (OWASP A01) — évite qu'un
// segment d'URL arbitraire (ex. /chants/abc) déclenche un GetById avec un id non-Guid.
const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isValidUuid(value: string | null | undefined): value is string {
  return typeof value === 'string' && UUID_REGEX.test(value);
}
