import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, Route } from '@angular/router';
import { of } from 'rxjs';
import { routes } from '@app/app.routes';
import { RoutePaths } from '@core/route-paths';
import { SidebarComponent } from '@app/components/layout/sidebar/sidebar.component';
import { ShellComponent } from '@app/components/layout/shell/shell.component';
import { AuthStore } from '@core/auth.store';
import { DisplayedZoneStore } from '@core/displayed-zone.store';
import { IDisplayedZone } from '@core/displayed-zone';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { MembershipRequestManagementService } from '@app/services/onboarding/membership-request-management.service';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

// Régression : la racine `/` n'était déclarée nulle part et tombait sur le wildcard `**`,
// donc sur la page 404 — au lieu d'être aiguillée vers la zone de l'utilisateur. Le test
// porte sur la table de routes plutôt que sur un Router monté : c'est la déclaration qui
// manquait, et l'aiguillage réel est déjà couvert par zone-resolver.spec.ts.
describe('routes', () => {
  const find = (path: string): Route | undefined => routes.find(route => route.path === path);

  it('déclare la racine, en correspondance exacte, redirigée vers /dashboard', () => {
    const root = find('');

    expect(root).toBeDefined();
    expect(root?.pathMatch).toBe('full');
    expect(root?.redirectTo).toBe(RoutePaths.Dashboard);
  });

  it('place la racine avant le wildcard, sinon `/` retomberait sur la page 404', () => {
    const rootIndex = routes.findIndex(route => route.path === '');
    const wildcardIndex = routes.findIndex(route => route.path === '**');

    expect(rootIndex).toBeGreaterThanOrEqual(0);
    expect(wildcardIndex).toBe(routes.length - 1);
    expect(rootIndex).toBeLessThan(wildcardIndex);
  });

  it('laisse /dashboard porter la règle d\'aiguillage (deux gardes, aucun composant propre)', () => {
    const dashboard = find(RoutePaths.Dashboard);

    expect(dashboard?.canActivate?.length).toBe(2);
    expect(dashboard?.redirectTo).toBeUndefined();
  });

  it('déclare la liste et le détail des chants sous /me', () => {
    const memberZone = find(RoutePaths.Me);
    const childPaths = memberZone?.children?.map(child => child.path);

    expect(childPaths).toContain(RoutePaths.Songs);
    expect(childPaths).toContain(RoutePaths.SongDetail);
  });
});

function buildMember(spaceType: SpaceTypeEnum, roles: string[]): IAuthenticatedUser {
  return {
    Id: 'member-1',
    Email: 'member@choralia.fr',
    Firstname: 'Camille',
    Lastname: 'Martin',
    Roles: [],
    SpaceRoles: [
      {
        SpaceId: 'space-1',
        Name: 'Espace actif',
        SpaceType: spaceType,
        Roles: roles,
        ClientId: null,
        ChoirId: null,
        PrimaryVoicePart: null
      }
    ],
    ClientRoles: []
  };
}

describe('navigation latérale', () => {
  const displayedZone = signal<IDisplayedZone>({ kind: 'member' });

  beforeEach(() => {
    sessionStorage.clear();
    displayedZone.set({ kind: 'member' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: DisplayedZoneStore, useValue: { zone: displayedZone } },
        { provide: MembershipRequestManagementService, useValue: { getPendingCount: () => of(0) } }
      ]
    });
    stubIconHttpRequests();
  });

  it('sur un espace Événement membre, ne présente ni Chants ni impasse de navigation', () => {
    TestBed.inject(AuthStore).setCurrentUser(buildMember(SpaceTypeEnum.Event, ['Participant']));
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Accueil');
    expect(text).not.toContain('Chants');
    expect(text).not.toContain('Activité');
    expect(text).not.toContain('Mon compte');
  });

  it('sépare le lien parent Chants du bouton qui déplie ses enfants', () => {
    TestBed.inject(AuthStore).setCurrentUser(buildMember(SpaceTypeEnum.Choir, ['Manager']));
    displayedZone.set({ kind: 'management', spaceId: 'space-1' });
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();

    const parentRows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.sidebar__nav-parent')
    );
    const songsRow = parentRows.find(row => row.querySelector('a')?.textContent?.includes('Chants'));
    const link = songsRow?.querySelector<HTMLAnchorElement>('a');
    const toggle = songsRow?.querySelector<HTMLButtonElement>('button');
    const parentHref = link?.getAttribute('href');

    expect(parentHref).toBe('/management/space-1/songs');
    expect(toggle?.getAttribute('aria-label')).toBe('Déplier Chants');

    toggle?.click();
    fixture.detectChanges();

    const childLinks = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>('.sidebar__sub-nav a')
    );
    expect(childLinks.map(child => child.textContent?.trim())).toEqual(['Partitions', 'Enregistrements']);
    expect(childLinks.map(child => child.getAttribute('href'))).toEqual([
      '/management/space-1/scores',
      '/management/space-1/recordings'
    ]);
    expect(link?.getAttribute('href')).toBe(parentHref);
  });

  it('piège le focus hors du contenu principal puis le restitue à la fermeture par Échap', async () => {
    TestBed.inject(AuthStore).setCurrentUser(buildMember(SpaceTypeEnum.Event, ['Participant']));
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const openButton = root.querySelector<HTMLButtonElement>('.topbar__nav-toggle');
    const closeButton = root.querySelector<HTMLButtonElement>('.sidebar__close-btn');
    if (!openButton || !closeButton) throw new Error('Contrôles de navigation absents du shell.');
    openButton.style.display = 'inline-flex';
    closeButton.style.display = 'inline-flex';
    openButton?.click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const main = root.querySelector<HTMLElement>('.shell__main');
    expect(document.activeElement).toBe(closeButton);
    expect(main?.hasAttribute('inert')).toBe(true);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(main?.hasAttribute('inert')).toBe(false);
    expect(document.activeElement).toBe(openButton);
  });
});
