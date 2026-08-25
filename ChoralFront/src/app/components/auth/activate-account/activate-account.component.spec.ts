import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { ActivateAccountComponent } from './activate-account.component';
import { environment } from '@env/environment';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const AUTH_BASE_URL = `${environment.apiUrl}auth`;
const VALID_PASSWORD = 'Sup3r!Secret';

/**
 * Dernière étape du parcours d'invitation : le lien reçu par mail porte userId et token en
 * query params, et ce sont eux qui partent dans le corps de POST /auth/ActivateAccount. Les
 * cas couverts sont ceux qui laisseraient un invité bloqué sans aucun signal : lien tronqué,
 * mot de passe refusé par la politique back, et lien déjà consommé (400).
 */
describe('ActivateAccountComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(request => request.url.startsWith('/icons/')).forEach(request => request.flush(''));
    httpMock.verify();
  });

  function render(userId: string | undefined, token: string | undefined) {
    const fixture = TestBed.createComponent(ActivateAccountComponent);
    fixture.componentRef.setInput('userId', userId);
    fixture.componentRef.setInput('token', token);
    fixture.detectChanges();
    return fixture;
  }

  it('lien complet : POST ActivateAccount avec UserId/Token/NewPassword, puis invitation à se connecter', () => {
    const fixture = render('user-1', 'tok+en/with=special');
    const component = fixture.componentInstance;

    component.form.setValue({ newPassword: VALID_PASSWORD, confirmPassword: VALID_PASSWORD });
    component.submit();

    const req = httpMock.expectOne(`${AUTH_BASE_URL}/ActivateAccount`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      UserId: 'user-1',
      Token: 'tok+en/with=special',
      NewPassword: VALID_PASSWORD
    });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();

    expect(component.submitted()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Votre compte est activé');
  });

  it('token absent : aucun appel HTTP, le formulaire n\'est même pas rendu', () => {
    const fixture = render('user-1', undefined);

    httpMock.expectNone(`${AUTH_BASE_URL}/ActivateAccount`);
    expect(fixture.componentInstance.hasValidParams()).toBe(false);
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('mot de passe hors politique back : aucun appel HTTP, message sous le champ', () => {
    const fixture = render('user-1', 'token-1');
    const component = fixture.componentInstance;

    component.form.setValue({ newPassword: 'motdepasse', confirmPassword: 'motdepasse' });
    component.submit();
    fixture.detectChanges();

    httpMock.expectNone(`${AUTH_BASE_URL}/ActivateAccount`);
    expect(component.form.controls.newPassword.touched).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('une majuscule');
  });

  it('mots de passe différents : aucun appel HTTP (validateur de groupe partagé)', () => {
    const fixture = render('user-1', 'token-1');
    const component = fixture.componentInstance;

    component.form.setValue({ newPassword: VALID_PASSWORD, confirmPassword: 'Autre!Chose1' });
    component.submit();
    fixture.detectChanges();

    httpMock.expectNone(`${AUTH_BASE_URL}/ActivateAccount`);
    expect(fixture.nativeElement.textContent).toContain('ne correspondent pas');
  });

  // Le back renvoie le MÊME 400 pour un jeton expiré, déjà consommé ou illisible : l'écran
  // ne doit donc afficher qu'un seul message, sans jamais laisser deviner la cause.
  it('400 du back : message d\'état inline, orientation vers une nouvelle invitation', () => {
    const fixture = render('user-1', 'token-1');
    const component = fixture.componentInstance;

    component.form.setValue({ newPassword: VALID_PASSWORD, confirmPassword: VALID_PASSWORD });
    component.submit();

    httpMock.expectOne(`${AUTH_BASE_URL}/ActivateAccount`).flush(
      { Message: 'Lien d\'activation invalide ou expiré.' },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();

    expect(component.submitted()).toBe(false);
    expect(component.isSubmitting()).toBe(false);
    expect(component.error()).toContain('invalide ou expiré');
  });
});
