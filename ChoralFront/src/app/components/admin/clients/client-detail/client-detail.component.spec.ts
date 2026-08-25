import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { ClientDetailComponent } from './client-detail.component';
import { ConfirmService } from '@app/services/confirm.service';
import { environment } from '@env/environment';
import { IClient } from '@models/admin-models/client.model';
import { ClientStatusEnum } from '@app/enums/status-client.enum';
import { percentageUsage } from '@app/services/admin/format-bytes.util';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;
const CLIENT_ID = '11111111-1111-1111-1111-111111111111';

function fakeClient(overrides: Partial<IClient> = {}): IClient {
  return {
    Id: CLIENT_ID,
    Name: 'Diocèse de Test',
    ContactName: null,
    ContactEmail: null,
    Status: ClientStatusEnum.Active,
    ChoirLimit: 10,
    MemberLimit: 100,
    StorageQuotaBytes: 1000,
    MaxFileSizeBytes: 100,
    ChoirCount: 1,
    MemberCount: 1,
    UsedStorageBytes: 1,
    ...overrides
  };
}

describe('ClientDetailComponent', () => {
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
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush(null));
    httpMock.verify();
  });

  function createLoaded(client: IClient) {
    const fixture = TestBed.createComponent(ClientDetailComponent);
    fixture.componentRef.setInput('id', client.Id);
    fixture.detectChanges();
    httpMock.expectOne(`${CLIENTS_BASE_URL}/${client.Id}`).flush(client);
    return { fixture, component: fixture.componentInstance };
  }

  it('suspension annulée : aucun appel réseau (ChangerStatut)', async () => {
    const { component } = createLoaded(fakeClient());
    confirmMock.confirm.mockResolvedValue(false);

    component.suspendreAction().subscribe({ error: () => undefined });

    const impactReq = httpMock.expectOne(`${CLIENTS_BASE_URL}/${CLIENT_ID}/SuspensionImpact`);
    impactReq.flush({ ChoirCount: 2, MemberCount: 5 });
    await Promise.resolve();
    await Promise.resolve();

    httpMock.expectNone(`${CLIENTS_BASE_URL}/ChangeStatus`);
  });

  it('suspension confirmée : émet un appel ChangerStatut réel, avec impacts chiffrés et motCleConfirmation = nom du client', async () => {
    const client = fakeClient();
    const { component } = createLoaded(client);
    confirmMock.confirm.mockResolvedValue(true);

    component.suspendreAction().subscribe();

    const impactReq = httpMock.expectOne(`${CLIENTS_BASE_URL}/${CLIENT_ID}/SuspensionImpact`);
    impactReq.flush({ ChoirCount: 2, MemberCount: 5 });
    await Promise.resolve();
    await Promise.resolve();

    expect(confirmMock.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        danger: true,
        confirmationKeyword: client.Name,
        impacts: ['2 chorale(s) concernée(s)', '5 membre(s) concerné(s)']
      })
    );

    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/ChangeStatus`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ Id: CLIENT_ID, Status: ClientStatusEnum.Suspended });
    req.flush(fakeClient({ Status: ClientStatusEnum.Suspended }));
  });

  it('réactivation en 409 (plafond dépassé) : message serveur affiché, pas un code brut', async () => {
    const { component } = createLoaded(fakeClient({ Status: ClientStatusEnum.Suspended }));

    component.reactivateAction().subscribe({ error: () => undefined });

    const req = httpMock.expectOne(`${CLIENTS_BASE_URL}/${CLIENT_ID}/Reactivate`);
    req.flush(
      { Message: 'Réactivation impossible, plafond dépassé : chorales 5/3.' },
      { status: 409, statusText: 'Conflict' }
    );

    expect(component.error()).toBe('Réactivation impossible, plafond dépassé : chorales 5/3.');
  });

  it('consommation à plus de 80% d’un plafond : mise en évidence appliquée (bg-danger + message)', () => {
    const { fixture } = createLoaded(fakeClient({ ChoirCount: 9, ChoirLimit: 10 }));
    fixture.componentInstance.selectTab('limits');
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Plafond bientôt atteint.');
    expect(fixture.nativeElement.querySelector('.progress-bar.bg-danger')).not.toBeNull();
  });

  it('plafond à 0 : pas de division par zéro, pas de 100% affiché', () => {
    expect(percentageUsage(5, 0)).toBe(0);
  });
});
