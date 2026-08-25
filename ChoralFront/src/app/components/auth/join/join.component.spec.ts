import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { JoinComponent } from './join.component';
import { AuthStore } from '@core/auth.store';
import { environment } from '@env/environment';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const ONBOARDING_BASE_URL = `${environment.apiUrl}onboarding`;

function buildUser(): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'user@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [],
    ClientRoles: []
  };
}

describe('RejoindreComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('code présent dans l\'URL : appelle PreviewCode et affiche le nom AVANT toute saisie', () => {
    const fixture = TestBed.createComponent(JoinComponent);
    fixture.componentRef.setInput('code', 'AB12-CD34');
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode` && r.params.get('code') === 'AB12-CD34');
    expect(req.request.method).toBe('GET');
    req.flush({ Name: 'Chorale Sainte-Cécile', SpaceType: SpaceTypeEnum.Choir });
    fixture.detectChanges();

    expect(fixture.componentInstance.preview()?.Name).toBe('Chorale Sainte-Cécile');
    expect(fixture.nativeElement.textContent).toContain('Chorale Sainte-Cécile');
  });

  it('code invalide : affiche le message unique du serveur tel quel, sans en deviner la cause', () => {
    const fixture = TestBed.createComponent(JoinComponent);
    fixture.componentRef.setInput('code', 'XXXX-XXXX');
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode`);
    req.flush({ Message: 'Code inconnu ou expiré.' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(fixture.componentInstance.previewError()).toBe('Code inconnu ou expiré.');
  });

  it('429 : affiche un message dédié distinct du message générique', () => {
    const fixture = TestBed.createComponent(JoinComponent);
    fixture.componentRef.setInput('code', 'AB12-CD34');
    fixture.detectChanges();

    const req = httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode`);
    req.flush({ Message: 'Trop de tentatives, merci de patienter.' }, { status: 429, statusText: 'Too Many Requests' });
    fixture.detectChanges();

    expect(fixture.componentInstance.previewError()).toBe('Trop de tentatives, merci de patienter.');
  });

  it('accessible non connecté : propose créer un compte / se connecter, jamais la demande directe', () => {
    const fixture = TestBed.createComponent(JoinComponent);
    fixture.componentRef.setInput('code', 'AB12-CD34');
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode`).flush({
      Name: 'Chorale Sainte-Cécile',
      SpaceType: SpaceTypeEnum.Choir
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.isAuthenticated()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Créer un compte');
  });

  it('accessible connecté : propose directement la demande à rejoindre', () => {
    TestBed.inject(AuthStore).setCurrentUser(buildUser());

    const fixture = TestBed.createComponent(JoinComponent);
    fixture.componentRef.setInput('code', 'AB12-CD34');
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/PreviewCode`).flush({
      Name: 'Chorale Sainte-Cécile',
      SpaceType: SpaceTypeEnum.Choir
    });
    fixture.detectChanges();

    expect(fixture.componentInstance.isAuthenticated()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Demander à rejoindre');
  });
});
