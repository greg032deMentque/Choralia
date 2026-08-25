import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { ScoreListComponent } from './score-list.component';
import { AuthStore } from '@core/auth.store';
import { environment } from '@env/environment';
import { IScore } from '@models/scores-models/score.model';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { ScoreTypeEnum } from '@app/enums/type-score.enum';
import { ScoreStatusEnum } from '@app/enums/status-score.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const SCORES_BASE_URL = `${environment.apiUrl}scores`;
const SONGS_BASE_URL = `${environment.apiUrl}songs`;
const SPACE_ID = 'espace-1';

function buildManagerUser(): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [
      { SpaceId: SPACE_ID, Name: 'Chorale A', SpaceType: SpaceTypeEnum.Choir, Roles: ['Manager'], ClientId: null, ChoirId: null, PrimaryVoicePart: null }
    ],
    ClientRoles: []
  };
}

const FAKE_SCORE: IScore = {
  Id: 'score-1',
  SongId: 'chant-1',
  Type: ScoreTypeEnum.General,
  TargetVoicePart: null,
  Version: 'v1',
  Status: ScoreStatusEnum.Draft,
  OwnerUserId: 'user-1',
  DownloadAllowed: true,
  OriginalFileName: 'partition.pdf',
  PublishedAt: null,
  CreatedAt: '2026-01-01T00:00:00Z'
};

// publish() peut recevoir un 409 (conflit de publication concurrente) — le composant doit
// distinguer ce cas d'une erreur générique en état inline, sans dupliquer le toast global de
// l'ApiErrorInterceptor.
describe('ScoreListComponent', () => {
  let component: ScoreListComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    });
    const fixture = TestBed.createComponent(ScoreListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('affiche un message spécifique sur conflit de publication concurrente (409)', () => {
    component.publish(FAKE_SCORE);

    const req = httpMock.expectOne(`${SCORES_BASE_URL}/score-1/Publish`);
    expect(req.request.method).toBe('POST');
    req.flush('Conflict', { status: 409, statusText: 'Conflict' });

    expect(component.error()).toBe(
      'Cette partition a été modifiée entre-temps (publication concurrente). Rechargez la liste avant de réessayer.'
    );
  });

  it('affiche un message générique sur une autre erreur de publication', () => {
    component.publish(FAKE_SCORE);

    const req = httpMock.expectOne(`${SCORES_BASE_URL}/score-1/Publish`);
    req.flush('Server error', { status: 500, statusText: 'Internal Server Error' });

    expect(component.error()).toBe('Impossible de publier cette partition.');
  });
});

/**
 * Une partition s'attache toujours à un chant : le bouton « Ajouter » exige un chant
 * sélectionné. Sur une chorale au répertoire vide — cas normal d'une chorale qui démarre —
 * l'écran n'affichait qu'un sélecteur vide et « Sélectionnez un chant », sans jamais dire que
 * le répertoire était vide ni où le remplir. Impasse muette.
 */
describe('ScoreListComponent — répertoire vide', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildManagerUser());
    authStore.setActiveSpace(SPACE_ID);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Même stratégie que testing/global-test-setup.ts : les requêtes GET /icons/*.svg sont
    // purement décoratives et partent de façon asynchrone (toObservable/effect dans
    // IconComponent). Elles sont neutralisées ici, AVANT verify() — le filet global s'exécute
    // trop tard, Vitest n'appelant pas les hooks suivants dès que verify() lève.
    httpMock.match(request => request.url.startsWith('/icons/')).forEach(request => request.flush(''));
    httpMock.verify();
    sessionStorage.clear();
  });

  function renderWithSongs(songs: { Id: string; Title: string }[]) {
    const fixture = TestBed.createComponent(ScoreListComponent);
    fixture.detectChanges();

    httpMock
      .expectOne(r => r.url === `${SONGS_BASE_URL}/GetPagedByChoir`)
      .flush({ Items: songs, TotalCount: songs.length, CurrentPage: 1, PageSize: 100 });
    fixture.detectChanges();

    return fixture;
  }

  it('répertoire vide : message explicite, lien vers les chants, aucun bouton « Ajouter »', () => {
    const fixture = renderWithSongs([]);

    const html: HTMLElement = fixture.nativeElement;
    expect(html.textContent).toContain('Aucun chant au répertoire');
    expect(html.querySelector<HTMLAnchorElement>('a.alert-link')?.textContent).toContain('Aller aux chants');
    expect(html.textContent).not.toContain('Ajouter une partition');
    expect(html.querySelector('app-song-picker select')).toBeNull();
  });

  it('répertoire non vide : le premier chant est sélectionné et le bouton « Ajouter » apparaît', () => {
    const fixture = renderWithSongs([{ Id: 'song-1', Title: 'Alléluia' }]);

    httpMock
      .expectOne(r => r.url === `${SCORES_BASE_URL}/GetPagedBySong`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    const html: HTMLElement = fixture.nativeElement;
    expect(fixture.componentInstance.selectedSongId()).toBe('song-1');
    expect(html.textContent).toContain('Ajouter une partition');
    expect(html.textContent).not.toContain('Aucun chant au répertoire');
  });

  // L'échec de chargement du répertoire ne passe pas par app-data-state (rendu seulement quand
  // un chant est sélectionné) : sans bloc dédié, l'erreur restait invisible à l'écran.
  it('échec de chargement du répertoire : l\'erreur est affichée, pas avalée', () => {
    const fixture = TestBed.createComponent(ScoreListComponent);
    fixture.detectChanges();

    httpMock
      .expectOne(r => r.url === `${SONGS_BASE_URL}/GetPagedByChoir`)
      .flush('Bad Request', { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Impossible de charger la liste des chants.');
  });
});
