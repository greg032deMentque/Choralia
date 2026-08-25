import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { StartComponent } from './start.component';
import { environment } from '@env/environment';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const ONBOARDING_BASE_URL = `${environment.apiUrl}onboarding`;

describe('DemarrerComponent', () => {
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

  it('0 rattachement et 0 demande : affiche les deux cartes, Rejoindre en action principale avec le champ code présent', () => {
    const fixture = TestBed.createComponent(StartComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${ONBOARDING_BASE_URL}/MyRequests?Page=1&PageSize=50`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 50 });
    fixture.detectChanges();

    const html: HTMLElement = fixture.nativeElement;
    expect(html.textContent).toContain('Rejoindre une chorale');
    expect(html.textContent).toContain('Créer une chorale');

    const primaryCard = html.querySelector('.start-card--primary');
    expect(primaryCard?.textContent).toContain('Rejoindre une chorale');
    expect(primaryCard?.querySelector('#start-code')).toBeTruthy();
  });

  it('demande en cours : affiche la variante "en attente" ET conserve toujours les deux actions de sortie', () => {
    const fixture = TestBed.createComponent(StartComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/MyRequests`).flush({
      Items: [
        { Id: 'demande-1', SpaceId: 'espace-1', SpaceName: 'Chorale Sainte-Cécile', Status: MembershipRequestStatusEnum.Pending, Message: null, CreatedAt: '2026-07-01' }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 50
    });
    fixture.detectChanges();

    const html: HTMLElement = fixture.nativeElement;
    expect(html.textContent).toContain('Chorale Sainte-Cécile');
    expect(html.textContent).toContain('En attente de validation');
    // Les deux actions de sortie restent affichées malgré la demande en cours.
    expect(html.textContent).toContain('Rejoindre une chorale');
    expect(html.textContent).toContain('Créer une chorale');
  });

  it("annulation d'une demande : émet l'appel DELETE et retire la carte de la liste", () => {
    const fixture = TestBed.createComponent(StartComponent);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === `${ONBOARDING_BASE_URL}/MyRequests`).flush({
      Items: [
        { Id: 'request-1', SpaceId: 'space-1', SpaceName: 'Chorale Sainte-Cécile', Status: MembershipRequestStatusEnum.Pending, Message: null, CreatedAt: '2026-07-01' }
      ],
      TotalCount: 1,
      CurrentPage: 1,
      PageSize: 50
    });
    fixture.detectChanges();

    const button = Array.from(fixture.nativeElement.querySelectorAll('button')).find(b => (b as HTMLElement).textContent?.trim() === 'Annuler') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    const deleteReq = httpMock.expectOne(`${ONBOARDING_BASE_URL}/MyRequests/request-1`);
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null);
    fixture.detectChanges();

    expect(fixture.componentInstance.pendingRequests().length).toBe(0);
    expect(fixture.nativeElement.textContent).not.toContain('Chorale Sainte-Cécile');
  });
});
