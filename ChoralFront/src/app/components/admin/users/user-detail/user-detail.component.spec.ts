import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { UserDetailComponent } from './user-detail.component';
import { ConfirmService } from '@app/services/confirm.service';
import { environment } from '@env/environment';
import { IAdminUserDetail } from '@models/admin-models/admin-user-detail.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { verifyIgnoringIcons } from '@app/testing/verify-ignoring-icons';

const ADMIN_USERS_BASE_URL = `${environment.apiUrl}admin-users`;

const FAKE_DETAIL: IAdminUserDetail = {
  Id: 'user-1',
  Email: 'jean.dupont@exemple.fr',
  Firstname: 'Jean',
  Lastname: 'Dupont',
  IsActive: true,
  IsGuestAccount: false,
  CreatedAt: '2026-01-01T00:00:00Z',
  LastConnection: null,
  LastActive: null,
  Choirs: [],
  Events: [],
  ClientAttachments: []
};

// ConfirmService (sweetalert2) est remplacé par un double contrôlable — on ne veut ni ouvrir
// une vraie modale sweetalert2 dans jsdom, ni dépendre de son DOM interne pour piloter la
// confirmation dans ces tests.
describe('UserDetailComponent', () => {
  let httpMock: HttpTestingController;
  let confirmMock: { confirm: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    confirmMock = { confirm: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
        { provide: ConfirmService, useValue: confirmMock }
      ]
    });
    // IconComponent (rendu via <app-icon> et DataStateComponent) charge ses SVG en HTTP sans
    // passer par HttpTestingController une fois stubbé — voir src/app/testing/icon-http-stub.ts.
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
    // Aucune route réelle n'est configurée dans ce test unitaire : la navigation post-
    // suppression (router.navigate) doit être neutralisée, sinon Angular Router rejette avec
    // NG04002 (aucune route ne matche '/admin/users') en promesse non interceptée.
    vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush(null));
    verifyIgnoringIcons(httpMock);
  });

  function createLoaded() {
    const fixture = TestBed.createComponent(UserDetailComponent);
    fixture.componentRef.setInput('id', 'user-1');
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/GetUserDetail` && r.params.get('userId') === 'user-1').flush(FAKE_DETAIL);
    return { fixture, component: fixture.componentInstance };
  }

  it('suppression annulée : aucun appel réseau (DELETE)', () => {
    const { component } = createLoaded();
    confirmMock.confirm.mockResolvedValue(false);

    component.deleteAction().subscribe({ error: () => undefined });

    httpMock.expectNone(r => r.url === `${ADMIN_USERS_BASE_URL}/Delete`);
  });

  it('suppression confirmée : émet un appel DELETE réel', async () => {
    const { component } = createLoaded();
    confirmMock.confirm.mockResolvedValue(true);

    component.deleteAction().subscribe();
    // La confirmation est asynchrone (Promise) : laisser la micro-tâche se résoudre avant
    // d'attendre la requête HTTP déclenchée par le switchMap.
    await Promise.resolve();
    await Promise.resolve();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/Delete` && r.params.get('userId') === 'user-1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it("409 (dernier administrateur) : message explicite affiché, l'utilisateur n'est pas retiré de l'affichage", async () => {
    const { component } = createLoaded();
    confirmMock.confirm.mockResolvedValue(true);

    component.deleteAction().subscribe({ error: () => undefined });
    await Promise.resolve();
    await Promise.resolve();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_USERS_BASE_URL}/Delete`);
    req.flush({ Message: 'Dernier administrateur' }, { status: 409, statusText: 'Conflict' });

    expect(component.error()).toBe('Impossible de supprimer le dernier administrateur.');
    expect(component.detail()).not.toBeNull();
    expect(component.detail()?.Id).toBe('user-1');
  });

  it('formulaire identité : champ obligatoire vide → soumission bloquée, aucun appel HTTP', () => {
    const { component } = createLoaded();
    component.startEditIdentity();
    component.form.controls.firstname.setValue('');

    component.submitIdentity().subscribe({ error: () => undefined });

    httpMock.expectNone(r => r.url === `${ADMIN_USERS_BASE_URL}/UpdateIdentity`);
    expect(component.form.controls.firstname.touched).toBe(true);
    expect(component.form.controls.firstname.invalid).toBe(true);
  });

  it('formulaire identité : email invalide → soumission bloquée, aucun appel HTTP', () => {
    const { component } = createLoaded();
    component.startEditIdentity();
    component.form.controls.email.setValue('pas-un-email');

    component.submitIdentity().subscribe({ error: () => undefined });

    httpMock.expectNone(r => r.url === `${ADMIN_USERS_BASE_URL}/UpdateIdentity`);
    expect(component.form.controls.email.invalid).toBe(true);
  });
});
