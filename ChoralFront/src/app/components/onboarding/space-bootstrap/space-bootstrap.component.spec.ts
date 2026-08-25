import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { provideToastr } from 'ngx-toastr';
import { By } from '@angular/platform-browser';
import { ComponentFixture } from '@angular/core/testing';
import { SpaceBootstrapComponent } from './space-bootstrap.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { ToastService } from '@app/services/toast.service';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { MemberStatusEnum } from '@app/enums/member-status.enum';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const CHOIR_MEMBERS_BASE_URL = `${environment.apiUrl}choir-members`;

// JoinCodePanelComponent (rendu inconditionnellement par space-bootstrap, hors
// périmètre de cette correction) lit son input requis `spaceId` DANS SON CONSTRUCTEUR
// (`constructor() { this.load(); }`) — un required input signal n'est jamais garanti disponible
// à ce stade lorsque le composant est instancié via un binding de template (seul le fixture
// racine d'un TestBed, via componentRef.setInput() avant le premier detectChanges(), bénéficie
// de cette garantie). Sans stub, monter ne serait-ce qu'une fois <app-space-bootstrap> lève
// NG0950 — bug préexistant, non introduit par cette correction, signalé séparément (voir
// récapitulatif) plutôt que corrigé ici (fichier hors périmètre du [CORRECTION CIBLÉE]).
@Component({ selector: 'app-join-code-panel', standalone: true, template: '' })
class JoinCodePanelStubComponent {
  readonly spaceId = input.required<string>();
}

function createComponent(spaceType: SpaceTypeEnum): ComponentFixture<SpaceBootstrapComponent> {
  const fixture = TestBed.createComponent(SpaceBootstrapComponent);
  fixture.componentRef.setInput('spaceId', 'choir-1');
  fixture.componentRef.setInput('spaceName', 'Chorale Sainte-Cécile');
  fixture.componentRef.setInput('spaceType', spaceType);
  fixture.detectChanges();
  return fixture;
}

function inviteButton(fixture: ComponentFixture<SpaceBootstrapComponent>): HTMLButtonElement {
  return fixture.debugElement.query(By.directive(SubmitOnceDirective)).nativeElement as HTMLButtonElement;
}

// Écran d'amorçage : branche Chorale (correction ciblée — POST /api/choir-members/Invite
// réel, remplace le mailto: du lot 6) testée ici. La branche Event (mailto: inchangé, aucun
// endpoint validé dans cette correction — voir commentaire de classe du composant) n'est pas
// retestée : son comportement n'a pas changé.
describe('SpaceBootstrapComponent — invitation choir', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    TestBed.overrideComponent(SpaceBootstrapComponent, {
      set: {
        imports: [ReactiveFormsModule, RouterLink, JoinCodePanelStubComponent, IconComponent, FormFieldComponent, SubmitOnceDirective]
      }
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('invitation valide : émet POST /api/choir-members/Invite avec le ChoirId de spaceId (jamais une autre valeur)', () => {
    const fixture = createComponent(SpaceTypeEnum.Choir);
    const component = fixture.componentInstance;

    component.inviteForm.setValue({ email: 'member@exemple.fr', firstname: 'Jean', lastname: 'Dupont' });
    inviteButton(fixture).click();

    const req = httpMock.expectOne(`${CHOIR_MEMBERS_BASE_URL}/Invite`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      ChoirId: 'choir-1',
      Email: 'member@exemple.fr',
      Firstname: 'Jean',
      Lastname: 'Dupont'
    });

    req.flush({
      Id: 'member-1',
      UserId: 'user-1',
      ChoirId: 'choir-1',
      Status: MemberStatusEnum.Invited,
      UserFullName: 'Jean Dupont',
      UserEmail: 'member@exemple.fr',
      Roles: [],
      SectionId: null,
      SectionVoicePart: null
    });
  });

  it('email invalide : soumission bloquée, message sous le champ, aucun appel HTTP', () => {
    const fixture = createComponent(SpaceTypeEnum.Choir);
    const component = fixture.componentInstance;

    component.inviteForm.controls.email.setValue('pas-un-email');
    inviteButton(fixture).click();
    fixture.detectChanges();

    httpMock.expectNone(`${CHOIR_MEMBERS_BASE_URL}/Invite`);
    expect(component.inviteForm.controls.email.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Adresse e-mail invalide');
  });

  // Le bouton réactivé fait partie du contrat : SubmitOnceDirective reste désactivée après un
  // succès, donc vider le formulaire ne suffisait pas — la deuxième invitation était impossible
  // sans recharger la page, alors que le commentaire du composant promettait l'inverse.
  it('succès : toast de confirmation, formulaire vidé ET bouton réactivé (pour enchaîner plusieurs invitations)', () => {
    const fixture = createComponent(SpaceTypeEnum.Choir);
    const component = fixture.componentInstance;
    const successSpy = vi.spyOn(TestBed.inject(ToastService), 'success');

    component.inviteForm.setValue({ email: 'member@exemple.fr', firstname: '', lastname: '' });
    const button = inviteButton(fixture);
    button.click();

    const req = httpMock.expectOne(`${CHOIR_MEMBERS_BASE_URL}/Invite`);
    req.flush({
      Id: 'member-1',
      UserId: 'user-1',
      ChoirId: 'choir-1',
      Status: MemberStatusEnum.Invited,
      UserFullName: null,
      UserEmail: 'member@exemple.fr',
      Roles: [],
      SectionId: null,
      SectionVoicePart: null
    });
    fixture.detectChanges();

    expect(successSpy).toHaveBeenCalledWith('Invitation envoyée à member@exemple.fr.');
    expect(component.inviteForm.controls.email.value).toBe('');
    expect(button.hasAttribute('disabled')).toBe(false);
  });

  it('échec serveur : le bouton est réactivé (pas de blocage définitif)', () => {
    const fixture = createComponent(SpaceTypeEnum.Choir);
    const component = fixture.componentInstance;

    component.inviteForm.setValue({ email: 'member@exemple.fr', firstname: '', lastname: '' });
    const button = inviteButton(fixture);
    button.click();

    const req = httpMock.expectOne(`${CHOIR_MEMBERS_BASE_URL}/Invite`);
    req.flush({ Message: 'Erreur serveur' }, { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    expect(button.hasAttribute('disabled')).toBe(false);
  });
});
