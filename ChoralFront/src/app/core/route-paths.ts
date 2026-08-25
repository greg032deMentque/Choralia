// Constantes de routes — jamais de magic strings dans les composants/guards/services.
//
// L'app est découpée en 4 zones (voir zone-resolver.ts pour la règle d'aiguillage) :
// /admin (claim global Admin), /client/:clientId (« Ma structure », ResponsableClient),
// /management/:spaceId (Responsable/SectionLeader/Organizer sur l'espace actif — reprend les
// écrans historiques du site de management), /me (appartenance simple, « Espace membre »).
// Les segments ci-dessous (Dashboard, Members, Songs, ...) sont réutilisés tels quels comme
// segments finaux de plusieurs arborescences (ex. RoutePaths.Dashboard sert de leaf à la fois
// sous /management/:spaceId et sous /admin) — un même nom de segment, jamais le même chemin
// absolu, donc aucune ambiguïté de routage.
export const RoutePaths = {
  Login: 'login',
  ForgotPassword: 'forgot-password',
  ResetPassword: 'reset-password',

  // Cible du lien d'invitation envoyé par mail : le back construit
  // {Frontend:BaseUrl}/activate-account?userId=...&token=... — ce segment DOIT rester
  // exactement 'activate-account'. Le lien a déjà été cassé deux fois par une divergence
  // de nommage entre back et front, d'où cette constante plutôt qu'un littéral.
  ActivateAccount: 'activate-account',

  // Registration auto-service (lot 6) — registration/confirm est un segment imbriqué (pas de
  // contenu propre à /registration seul).
  Registration: 'registration',
  RegistrationConfirm: 'confirm',
  VerifyEmail: 'verify-email',

  // Accessible connecté ET non connecté (ni guestGuard ni authGuard) : voir
  // join.component pour le mode de garde retenu (route publique, branchement interne).
  Join: 'join',

  // Hub post-connexion pour un utilisateur sans aucun rattachement (remplace l'ancienne
  // redirection vers NoSpace — voir zone-resolver.ts). CreateChoir/CreateEvent sont
  // des segments enfants de Start, jamais des routes racine.
  Start: 'start',
  StartCreateChoir: 'create-choir',
  StartCreateEvent: 'create-event',

  // Route « intelligente » : jamais de contenu propre, redirige toujours (zoneRedirectGuard)
  // vers la zone réelle de l'utilisateur (AuthStore.currentZone()). Sert de target neutre et
  // stable pour guestGuard, le fallback de returnUrl, et NotFoundComponent (composant figé du
  // lot 1a — on ne modifie pas sa target de redirection, on la fait pointer ici).
  Dashboard: 'dashboard',

  Admin: 'admin',
  AdminDashboard: 'dashboard',
  AdminClients: 'clients',
  AdminChoirs: 'choirs',
  AdminEvents: 'events',
  AdminUsers: 'users',
  AdminSongs: 'songs',
  AdminAudit: 'audit',

  Client: 'client',
  // Enfants de /client/:clientId — nom distinct d'AdminChoirs (même valeur de segment
  // 'choirs', arborescence différente) : détail chorale de la zone « Ma structure », pas la
  // liste transverse de l'administration générale.
  ClientChoirs: 'choirs',
  ClientChoirDetail: 'choirs/:choirId',

  Management: 'management',

  Me: 'me',

  NoSpace: 'no-space',

  // Segments finaux réutilisés sous /management/:spaceId (et, pour Dashboard, sous /admin).
  Members: 'members',
  MemberDetail: 'members/:id',
  Songs: 'songs',
  SongDetail: 'songs/:id',
  Scores: 'scores',
  Recordings: 'recordings',
  SongLists: 'song-lists',
  SongListDetail: 'song-lists/:id',
  Events: 'events',
  EventDetail: 'events/:id',
  // Pas de segment « instructions » : les consignes se gèrent dans l'écran du chant, il n'existe
  // aucune route transverse (décision produit, Spec/chorale/10-decisions.md).
  Activity: 'activity'
} as const;

// Segments finaux valides sous /management/:spaceId — utilisé uniquement pour valider un
// returnUrl (voir isAllowedReturnUrl), pas pour construire le routing lui-même (app.routes.ts
// reste la source de vérité du routage).
const MANAGEMENT_LEAF_SEGMENTS: readonly string[] = [
  RoutePaths.Dashboard,
  RoutePaths.Members,
  RoutePaths.Songs,
  RoutePaths.Scores,
  RoutePaths.Recordings,
  RoutePaths.SongLists,
  RoutePaths.Events,
  RoutePaths.Activity
];

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Construit un chemin absolu sous /management/:spaceId — seul point de concaténation, pour ne
// pas dupliquer `/${RoutePaths.Management}/${spaceId}/...` dans chaque composant de feature.
export function managementPath(spaceId: string, ...segments: string[]): string {
  return `/${RoutePaths.Management}/${spaceId}${segments.length > 0 ? `/${segments.join('/')}` : ''}`;
}

// Routes autorisées comme target de redirection post-login (returnUrl). Toute valeur hors
// de cette structure est rejetée — jamais de redirection vers une URL arbitraire (OWASP A01).
// Les segments dynamiques (spaceId, clientId) sont validés par forme (UUID), pas par valeur
// exacte : le contrôle d'accès réel reste porté par les guards (spaceRoleGuard/clientRoleGuard),
// cette fonction ne fait que fermer l'open redirect.
export function isAllowedReturnUrl(url: string | null | undefined): boolean {
  if (!url) return false;
  if (!url.startsWith('/') || url.startsWith('//')) return false;

  const [path] = url.split('?');
  const segments = path.split('/').filter(Boolean);
  if (segments.length === 0) return false;

  switch (segments[0]) {
    case RoutePaths.Dashboard:
    case RoutePaths.Me:
    case RoutePaths.NoSpace:
    case RoutePaths.Start:
      return segments.length === 1;
    case RoutePaths.Admin:
      return true;
    case RoutePaths.Client:
      return segments.length >= 2 && UUID_REGEX.test(segments[1]);
    case RoutePaths.Management:
      if (segments.length < 2 || !UUID_REGEX.test(segments[1])) return false;
      if (segments.length === 2) return true;
      return MANAGEMENT_LEAF_SEGMENTS.includes(segments[2]);
    default:
      return false;
  }
}
