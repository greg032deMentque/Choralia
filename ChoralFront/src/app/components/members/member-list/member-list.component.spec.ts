import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { MemberListComponent } from './member-list.component';
import { AuthStore } from '@core/auth.store';
import { environment } from '@env/environment';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { ToastService } from '@app/services/toast.service';
import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { vi } from 'vitest';

const SPACE_ID = 'espace-1';
const MEMBERS_BASE_URL = `${environment.apiUrl}choir-members`;
const MEMBERSHIP_REQUESTS_BASE_URL = `${environment.apiUrl}spaces/${SPACE_ID}/MembershipRequests`;

function buildUser(roles: string[]): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [
      { SpaceId: SPACE_ID, Name: 'Chorale A', SpaceType: SpaceTypeEnum.Choir, Roles: roles, ClientId: null, ChoirId: null, PrimaryVoicePart: null }
    ],
    ClientRoles: []
  };
}

describe('MembreListComponent — segment Demandes', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('ChefPupitre : le segment Demandes est structurellement absent du DOM (pas seulement masqué)', () => {
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser(['SectionLeader']));
    authStore.setActiveSpace(SPACE_ID);

    const fixture = TestBed.createComponent(MemberListComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${MEMBERS_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    // Aucun appel de comptage de demandes n'est même émis pour un SectionLeader.
    httpMock.expectNone(r => r.url === `${MEMBERSHIP_REQUESTS_BASE_URL}/GetPaged`);

    const html: HTMLElement = fixture.nativeElement;
    expect(html.querySelector('app-requests-adhesion-list')).toBeNull();
    expect(html.textContent).not.toContain('Requests');
  });

  it('Responsable : le segment Demandes est disponible avec un badge quand des demandes sont en attente', () => {
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser(['Manager']));
    authStore.setActiveSpace(SPACE_ID);

    const fixture = TestBed.createComponent(MemberListComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${MEMBERS_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    httpMock.expectOne(r => r.url === `${MEMBERSHIP_REQUESTS_BASE_URL}/GetPaged`).flush({
      Items: [{ Id: 'd1', SpaceId: SPACE_ID, UserId: 'u1', UserFullName: 'Ada', UserEmail: null, Status: 0, Message: null, DeclineReason: null, CreatedAt: '2026-01-01', HandledAt: null }],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 100
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Demandes');
    expect(fixture.componentInstance.pendingRequestsCount()).toBe(1);
  });
});

/**
 * Invitation d'un membre depuis l'écran Membres. Le back faisait déjà tout le travail
 * (création du compte à EmailConfirmed=false, envoi d'un lien de création de mot de passe,
 * membre en statut Invité) : seule l'UI manquait, `ChoirMembersService.invite` n'étant appelé
 * que depuis l'écran d'amorçage post-création de chorale.
 */
describe('MembreListComponent — invitation', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser(['Manager']));
    authStore.setActiveSpace(SPACE_ID);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(request => request.url.startsWith('/icons/')).forEach(request => request.flush(''));
    httpMock.verify();
  });

  function renderWithInvitePanelOpen() {
    const fixture = TestBed.createComponent(MemberListComponent);
    fixture.detectChanges();

    flushInitialLoad();
    fixture.componentInstance.toggleInviteForm();
    fixture.detectChanges();

    return fixture;
  }

  // Assertion explicite plutôt qu'un `!` : si le bouton disparaît du DOM, le test échoue avec
  // un message qui nomme la cause, au lieu d'un TypeError opaque.
  function clickInviteSubmit(html: HTMLElement): void {
    const button = html.querySelector<HTMLButtonElement>('form button.btn-primary');
    if (!button) throw new Error("Bouton d'envoi de l'invitation absent du DOM");
    button.click();
  }

  function flushInitialLoad(): void {
    httpMock.expectOne(r => r.url === `${MEMBERS_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    httpMock.expectOne(r => r.url === `${MEMBERSHIP_REQUESTS_BASE_URL}/GetPaged`).flush({
      Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 100
    });
  }

  it('SectionLeader : aucun bouton d\'invitation (même garde que le segment Demandes)', () => {
    const authStore = TestBed.inject(AuthStore);
    authStore.setCurrentUser(buildUser(['SectionLeader']));

    const fixture = TestBed.createComponent(MemberListComponent);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === `${MEMBERS_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Inviter un membre');
  });

  // Le ChoirId du corps DOIT être celui de l'espace actif : la policy ChoirManager le compare à
  // l'en-tête X-Space-Id, et une autre valeur enverrait l'invitation vers le mauvais espace.
  it('invitation valide : POST Invite avec le ChoirId de l\'espace actif, toast, panneau refermé, liste rechargée', () => {
    const fixture = renderWithInvitePanelOpen();
    const component = fixture.componentInstance;
    const successSpy = vi.spyOn(TestBed.inject(ToastService), 'success');

    component.inviteForm.setValue({
      email: 'nouveau@exemple.fr',
      firstname: 'Ada',
      lastname: 'Lovelace',
      primaryVoicePart: VoicePartEnum.Soprano
    });
    clickInviteSubmit(fixture.nativeElement);

    const req = httpMock.expectOne(`${MEMBERS_BASE_URL}/Invite`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      ChoirId: SPACE_ID,
      Email: 'nouveau@exemple.fr',
      Firstname: 'Ada',
      Lastname: 'Lovelace',
      PrimaryVoicePart: VoicePartEnum.Soprano
    });
    req.flush({ Id: 'm1', UserId: 'u1', ChoirId: SPACE_ID, Status: 0, UserFullName: 'Ada Lovelace', UserEmail: 'nouveau@exemple.fr', Roles: [], SectionId: null, SectionVoicePart: null });

    expect(successSpy).toHaveBeenCalledWith('Invitation envoyée à nouveau@exemple.fr.');
    expect(component.showInviteForm()).toBe(false);
    httpMock.expectOne(r => r.url === `${MEMBERS_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  // Le back accepte PrimaryVoicePart nul (fenêtre de déploiement back-avant-front) : c'est
  // l'UI, et elle seule, qui garantit qu'aucun membre n'est invité sans pupitre.
  it('voix non renseignée : aucun appel HTTP, message sous le champ', () => {
    const fixture = renderWithInvitePanelOpen();
    const component = fixture.componentInstance;

    component.inviteForm.controls.email.setValue('nouveau@exemple.fr');
    clickInviteSubmit(fixture.nativeElement);
    fixture.detectChanges();

    httpMock.expectNone(`${MEMBERS_BASE_URL}/Invite`);
    expect(component.inviteForm.controls.primaryVoicePart.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Voix est obligatoire');
  });

  it('email invalide : aucun appel HTTP, message sous le champ', () => {
    const fixture = renderWithInvitePanelOpen();
    const component = fixture.componentInstance;

    component.inviteForm.controls.email.setValue('pas-un-email');
    component.inviteForm.controls.primaryVoicePart.setValue(VoicePartEnum.Alto);
    clickInviteSubmit(fixture.nativeElement);
    fixture.detectChanges();

    httpMock.expectNone(`${MEMBERS_BASE_URL}/Invite`);
    expect(component.inviteForm.controls.email.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Adresse e-mail invalide');
  });
});
