import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { RecordingListComponent } from './recording-list.component';
import { AuthStore } from '@core/auth.store';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const CHOIR_ID = 'chorale-1';

function buildUser(globalRoles: string[], spaceRoles: string[]): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: globalRoles,
    SpaceRoles: [
      { SpaceId: CHOIR_ID, Name: 'Chorale Test', SpaceType: SpaceTypeEnum.Choir, Roles: spaceRoles, ClientId: null, ChoirId: null, PrimaryVoicePart: null }
    ],
    ClientRoles: []
  };
}

// canManage/canPublish (protected, accès via [] volontaire pour les tester sans exposer
// l'API publique du composant) — Publish/Reject réservés au Responsable, pas de délégation
// SectionLeader dans ce lot (décision documentée bloc de transfert).
describe('RecordingListComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent(): { component: RecordingListComponent; authStore: AuthStore } {
    const fixture = TestBed.createComponent(RecordingListComponent);
    const authStore = TestBed.inject(AuthStore);
    return { component: fixture.componentInstance, authStore };
  }

  it('Responsable de la chorale actif : canManage et canPublish sont autorisés', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser([], ['Manager']));

    expect(component['canManage']()).toBe(true);
    expect(component['canPublish']()).toBe(true);
  });

  it('SectionLeader de la chorale actif : canPublish est refusé', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser([], ['SectionLeader']));

    expect(component['canPublish']()).toBe(false);
  });

  it('SectionLeader de la chorale actif : canManage est autorisé', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser([], ['SectionLeader']));

    expect(component['canManage']()).toBe(true);
  });

  it('Administrateur général (claim JWT global Admin) : canManage et canPublish sont autorisés même sans rôle chorale', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser(['Admin'], []));

    expect(component['canManage']()).toBe(true);
    expect(component['canPublish']()).toBe(true);
  });

  it('Aucun rôle sur la chorale actif : canManage et canPublish sont refusés', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser([], []));

    expect(component['canManage']()).toBe(false);
    expect(component['canPublish']()).toBe(false);
  });

  it('Membre simple (Chanteur) seul sur la chorale actif : canManage est refusé', () => {
    const { component, authStore } = createComponent();
    authStore.setCurrentUser(buildUser([], ['Singer']));

    expect(component['canManage']()).toBe(false);
  });
});

/**
 * Un enregistrement s'attache toujours à un chant : le bouton « Ajouter » exige un chant
 * sélectionné. Sur une chorale au répertoire vide — cas normal d'une chorale qui démarre —
 * l'écran n'affichait qu'un sélecteur vide et « Sélectionnez un chant », sans jamais dire que
 * le répertoire était vide ni où le remplir. Impasse muette.
 */
describe('RecordingListComponent — répertoire vide', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser([], ['Manager']));
    authStore.setActiveSpace(CHOIR_ID);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Voir score-list.component.spec.ts : les requêtes d'icônes sont décoratives et
    // asynchrones, neutralisées ici avant verify().
    httpMock.match(request => request.url.startsWith('/icons/')).forEach(request => request.flush(''));
    httpMock.verify();
    sessionStorage.clear();
  });

  function renderWithSongs(songs: { Id: string; Title: string }[]) {
    const fixture = TestBed.createComponent(RecordingListComponent);
    fixture.detectChanges();

    httpMock
      .expectOne(r => r.url === `${environment.apiUrl}songs/GetPagedByChoir`)
      .flush({ Items: songs, TotalCount: songs.length, CurrentPage: 1, PageSize: 100 });
    fixture.detectChanges();

    return fixture;
  }

  it('répertoire vide : message explicite, lien vers les chants, aucun bouton « Ajouter »', () => {
    const fixture = renderWithSongs([]);

    const html: HTMLElement = fixture.nativeElement;
    expect(html.textContent).toContain('Aucun chant au répertoire');
    expect(html.querySelector<HTMLAnchorElement>('a.alert-link')?.textContent).toContain('Aller aux chants');
    expect(html.textContent).not.toContain('Ajouter un enregistrement');
    expect(html.querySelector('app-song-picker select')).toBeNull();
  });

  it('répertoire non vide : le premier chant est sélectionné et le bouton « Ajouter » apparaît', () => {
    const fixture = renderWithSongs([{ Id: 'song-1', Title: 'Alléluia' }]);

    httpMock
      .expectOne(r => r.url === `${environment.apiUrl}recordings/GetPagedBySong`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    const html: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.selectedSongId()).toBe('song-1');
    expect(html.textContent).toContain('Ajouter un enregistrement');
    expect(html.textContent).not.toContain('Aucun chant au répertoire');
  });
});
