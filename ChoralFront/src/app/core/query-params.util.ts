import { ParamMap } from '@angular/router';
import { isValidUuid } from '@core/uuid.util';

// Lecture défensive des query params de filtre transmis par une navigation externe (ex. tuile
// du tableau de bord admin — voir dashboard.component.ts) : utilisé UNIQUEMENT pour
// l'INITIALISATION des signaux de filtre au chargement, à partir d'un ActivatedRouteSnapshot
// (lecture unique, jamais une souscription continue). Toute valeur absente, malformée ou hors
// énumération connue est ignorée silencieusement — une URL trafiquée ne doit jamais faire
// planter un écran ni produire un filtre incohérent (OWASP A01). Les filtres restent ensuite
// modifiables par l'utilisateur via les signaux existants du composant, jamais reliés à ce
// paramMap au-delà du chargement initial.

export function parseBooleanQueryParam(paramMap: ParamMap, key: string): boolean | undefined {
  const raw = paramMap.get(key);
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  return undefined;
}

// Forme tri-état ('' | 'true' | 'false') attendue par les signaux de filtre booléen existants
// (ex. filterInactive30j, filterUpcoming) — évite de répéter la conversion dans chaque composant.
export function parseTriStateBooleanQueryParam(paramMap: ParamMap, key: string): '' | 'true' | 'false' {
  const value = parseBooleanQueryParam(paramMap, key);
  return value === undefined ? '' : value ? 'true' : 'false';
}

// `allowedValues` est la liste blanche exacte de l'enum côté composant (ex. allStatuss) —
// jamais une simple vérification de plage numérique, pour reject un entier syntaxiquement
// valide mais hors énumération métier.
export function parseEnumQueryParam<T extends number>(paramMap: ParamMap, key: string, allowedValues: readonly T[]): T | undefined {
  const raw = paramMap.get(key);
  if (raw === null) return undefined;
  const parsed = Number(raw);
  if (!Number.isInteger(parsed)) return undefined;
  return (allowedValues as readonly number[]).includes(parsed) ? (parsed as T) : undefined;
}

export function parseGuidQueryParam(paramMap: ParamMap, key: string): string | undefined {
  const raw = paramMap.get(key);
  return isValidUuid(raw) ? raw : undefined;
}

// Query param répété (`?ClientIds=a&ClientIds=b`) — ParamMap.getAll gère déjà la forme
// unique-ou-répétée transparemment. Les valeurs individuellement invalides sont écartées sans
// invalider les autres ; un résultat vide redevient `undefined` (paramètre absent) plutôt qu'un
// tableau vide, pour rester cohérent avec appendOptionalParam côté service.
export function parseGuidListQueryParam(paramMap: ParamMap, key: string): string[] | undefined {
  const values = paramMap.getAll(key).filter(isValidUuid);
  return values.length > 0 ? values : undefined;
}
