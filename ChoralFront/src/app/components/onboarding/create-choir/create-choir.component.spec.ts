import { Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, RouterLink } from '@angular/router';
import { ReactiveFormsModule } from '@angular/forms';
import { provideToastr } from 'ngx-toastr';
import { CreateChoirComponent } from './create-choir.component';
import { SpaceBootstrapComponent } from '@app/components/onboarding/space-bootstrap/space-bootstrap.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { AuthStore } from '@core/auth.store';
import { environment } from '@env/environment';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

// JoinCodePanelComponent (rendu par SpaceBootstrapComponent, lui-même rendu par ce
// composant une fois `created()` renseigné) lit son input requis `spaceId` dans son
// CONSTRUCTEUR — jamais garanti disponible à ce stade pour un composant instancié via un
// binding de template. Bug préexistant, hors périmètre de cette correction (signalé
// séparément) : stubbé ici pour ne pas faire échouer ce test sur un NG0950 sans rapport avec
// ce qui est vérifié (le positionnement de l'espace actif).
@Component({ selector: 'app-join-code-panel', standalone: true, template: '' })
class JoinCodePanelStubComponent {
  readonly spaceId = input.required<string>();
}

const ONBOARDING_BASE_URL = `${environment.apiUrl}onboarding`;

function buildUser(): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'responsable@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [],
    ClientRoles: []
  };
}

describe('CreateChoirComponent', () => {
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
    TestBed.inject(AuthStore).setCurrentUser(buildUser());
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('champ Structure vide : ne transmet ni chaîne vide ni clé Structure présente avec une valeur vide', () => {
    const fixture = TestBed.createComponent(CreateChoirComponent);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ name: 'Chorale Sainte-Cécile', description: '', structure: '' });
    fixture.componentInstance.submit();

    const req = httpMock.expectOne(`${ONBOARDING_BASE_URL}/CreateChoir`);
    expect(req.request.body.Structure).toBeUndefined();
    expect(req.request.body.Description).toBeUndefined();
    req.flush({ Id: 'space-1', ClientId: 'client-1', Name: 'Chorale Sainte-Cécile', Description: null, ImageUrl: null, Status: 0 });

    httpMock.expectOne(`${environment.apiUrl}auth/Me`).flush(buildUser());
  });

  it('champs obligatoires manquants : soumission bloquée, aucun appel HTTP, message sous le champ', () => {
    const fixture = TestBed.createComponent(CreateChoirComponent);
    fixture.detectChanges();

    fixture.componentInstance.submit();
    fixture.detectChanges();

    httpMock.expectNone(`${ONBOARDING_BASE_URL}/CreateChoir`);
    expect(fixture.componentInstance.form.controls.name.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Le nom est obligatoire');
  });

  // Garde contre le piège identifié en correction ciblée : après création, AuthStore.activeSpaceId
  // doit déjà pointer vers la chorale créée avant que l'écran d'amorçage (et son invitation par
  // email, POST /api/choir-members/Invite scopé par X-Space-Id) ne soit rendu — sinon
  // l'invitation partirait avec un espace actif nul ou vers un AUTRE espace de l'utilisateur.
  it("après création, l'espace actif est bien la chorale créée avant tout rendu de l'écran d'amorçage", () => {
    const fixture = TestBed.createComponent(CreateChoirComponent);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ name: 'Chorale Sainte-Cécile', description: '', structure: '' });
    fixture.componentInstance.submit();

    const req = httpMock.expectOne(`${ONBOARDING_BASE_URL}/CreateChoir`);
    req.flush({ Id: 'space-nouveau', ClientId: 'client-1', Name: 'Chorale Sainte-Cécile', Description: null, ImageUrl: null, Status: 0 });

    // AuthStore.setActiveSpace doit être posé AVANT que le composant n'émette `created` — la
    // requête GET /auth/Me (rafraîchissement de session déclenché par OnboardingService.createChoir)
    // fait partie de la même chaîne synchrone que ce positionnement.
    httpMock.expectOne(`${environment.apiUrl}auth/Me`).flush(buildUser());
    fixture.detectChanges();

    expect(TestBed.inject(AuthStore).activeSpaceId()).toBe('space-nouveau');
    expect(fixture.componentInstance.created()?.Id).toBe('space-nouveau');
  });

  it('403 (email non vérifié) : affiche un écran de blocage explicite avec renvoi de lien', () => {
    const fixture = TestBed.createComponent(CreateChoirComponent);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({ name: 'Chorale Sainte-Cécile', description: '', structure: '' });
    fixture.componentInstance.submit();

    const req = httpMock.expectOne(`${ONBOARDING_BASE_URL}/CreateChoir`);
    req.flush(
      { Message: 'Vérifiez votre adresse email avant de créer une chorale ou un événement.' },
      { status: 403, statusText: 'Forbidden' }
    );
    fixture.detectChanges();

    expect(fixture.componentInstance.emailNonVerifie()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Vérifiez votre adresse email');
    expect(fixture.nativeElement.textContent).toContain('Renvoyer le lien de vérification');
  });
});
