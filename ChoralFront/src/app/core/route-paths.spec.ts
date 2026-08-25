import { RoutePaths, isAllowedReturnUrl } from '@core/route-paths';

// isAllowedReturnUrl ferme l'open redirect (OWASP A01) sur le returnUrl post-login. Ce test
// fige le comportement au moment du renommage des routes (voir route-paths.ts) : toutes les
// routes ont été traduites en anglais (ex. 'songs' -> 'songs', 'moi' -> 'me') sauf login/
// forgot-password/reset-password/verify-email, câblées en dur côté back dans les liens email.
describe('isAllowedReturnUrl', () => {
  it('accepte les segments racine simples (dashboard, me, no-space, start)', () => {
    expect(isAllowedReturnUrl(`/${RoutePaths.Dashboard}`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.Me}`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.NoSpace}`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.Start}`)).toBe(true);
  });

  it('rejette un segment racine simple avec un sous-segment supplémentaire', () => {
    expect(isAllowedReturnUrl(`/${RoutePaths.Me}/extra`)).toBe(false);
    expect(isAllowedReturnUrl(`/${RoutePaths.Start}/extra`)).toBe(false);
  });

  it('accepte toute sous-route /admin (le contrôle d\'accès réel reste porté par adminGuard)', () => {
    expect(isAllowedReturnUrl(`/${RoutePaths.Admin}`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.Admin}/${RoutePaths.AdminSongs}`)).toBe(true);
  });

  it('accepte /client/:clientId uniquement avec un clientId au format UUID', () => {
    expect(isAllowedReturnUrl(`/${RoutePaths.Client}/019fba3a-f3be-7197-accc-9b75f1e63505`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.Client}/pas-un-uuid`)).toBe(false);
    expect(isAllowedReturnUrl(`/${RoutePaths.Client}`)).toBe(false);
  });

  it('accepte /management/:spaceId avec un spaceId UUID, sans segment final', () => {
    expect(isAllowedReturnUrl(`/${RoutePaths.Management}/019fba3a-f3be-7197-accc-9b75f1e63505`)).toBe(true);
    expect(isAllowedReturnUrl(`/${RoutePaths.Management}/pas-un-uuid`)).toBe(false);
  });

  it('accepte /management/:spaceId/<segment final connu> pour chaque nouveau segment anglais', () => {
    const spaceId = '019fba3a-f3be-7197-accc-9b75f1e63505';
    const knownLeaves = [
      RoutePaths.Dashboard,
      RoutePaths.Members,
      RoutePaths.Songs,
      RoutePaths.Scores,
      RoutePaths.Recordings,
      RoutePaths.SongLists,
      RoutePaths.Events,
      RoutePaths.Activity
    ];

    for (const leaf of knownLeaves) {
      expect(isAllowedReturnUrl(`/${RoutePaths.Management}/${spaceId}/${leaf}`)).toBe(true);
    }
  });

  it('rejette un segment final inconnu sous /management/:spaceId (ex. ancien segment français résiduel)', () => {
    const spaceId = '019fba3a-f3be-7197-accc-9b75f1e63505';
    expect(isAllowedReturnUrl(`/${RoutePaths.Management}/${spaceId}/chants`)).toBe(false);
    expect(isAllowedReturnUrl(`/${RoutePaths.Management}/${spaceId}/consignes`)).toBe(false);
    expect(isAllowedReturnUrl(`/${RoutePaths.Management}/${spaceId}/inconnu`)).toBe(false);
  });

  it('ferme l\'open redirect : rejette une URL absolue, un protocole-relatif, ou une racine inconnue', () => {
    expect(isAllowedReturnUrl('https://evil.example.com')).toBe(false);
    expect(isAllowedReturnUrl('//evil.example.com')).toBe(false);
    expect(isAllowedReturnUrl('/inconnu')).toBe(false);
    expect(isAllowedReturnUrl(null)).toBe(false);
    expect(isAllowedReturnUrl(undefined)).toBe(false);
    expect(isAllowedReturnUrl('')).toBe(false);
  });
});
