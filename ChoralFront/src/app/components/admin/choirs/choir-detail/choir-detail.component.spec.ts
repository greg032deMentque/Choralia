import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { ChoirDetailComponent } from './choir-detail.component';
import { ConfirmService } from '@app/services/confirm.service';
import { environment } from '@env/environment';
import { IAdminChoirDetail } from '@models/admin-models/admin-choir-detail.model';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const ADMIN_CHOIRS_BASE_URL = `${environment.apiUrl}admin-choirs`;

function fakeDetail(overrides: Partial<IAdminChoirDetail> = {}): IAdminChoirDetail {
  return {
    Id: '11111111-1111-1111-1111-111111111111',
    Name: 'Sainte-Cécile',
    Description: null,
    ImageUrl: null,
    ClientId: 'client-1',
    ClientName: 'Diocèse',
    Status: ChoirStatusEnum.Published,
    CreatedAt: '2026-01-01T00:00:00Z',
    MemberCount: 0,
    SongCount: 0,
    EventCount: 0,
    ClientChoirLimit: 10,
    ClientChoirCount: 1,
    ClientMemberLimit: 100,
    ClientMemberCount: 1,
    ClientStorageQuotaBytes: 1000,
    ClientUsedStorageBytes: 1,
    ...overrides
  };
}

describe('ChoraleDetailComponent', () => {
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

  function createLoaded(detail: IAdminChoirDetail) {
    const fixture = TestBed.createComponent(ChoirDetailComponent);
    fixture.componentRef.setInput('id', detail.Id);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/${detail.Id}`).flush(detail);
    return { fixture, component: fixture.componentInstance };
  }

  it('statut Publié : seules les transitions Annulé/Archivé sont proposées', () => {
    const { component } = createLoaded(fakeDetail({ Status: ChoirStatusEnum.Published }));

    expect(component.transitionsAllowed()).toEqual([ChoirStatusEnum.Cancelled, ChoirStatusEnum.Archived]);
  });

  it('statut Archivé : seule la transition Publié est proposée', () => {
    const { component } = createLoaded(fakeDetail({ Status: ChoirStatusEnum.Archived }));

    expect(component.transitionsAllowed()).toEqual([ChoirStatusEnum.Published]);
  });

  it('passage à Archivé : les impacts chiffrés (GetImpactArchivage) sont affichés dans la confirmation', async () => {
    const { component } = createLoaded(fakeDetail({ Status: ChoirStatusEnum.Published }));
    confirmMock.confirm.mockResolvedValue(false);

    component.statusAction(ChoirStatusEnum.Archived)().subscribe({ error: () => undefined });

    const impactReq = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/11111111-1111-1111-1111-111111111111/ArchiveImpact`);
    impactReq.flush({ MemberCount: 12, SongCount: 34, EventCount: 5 });
    await Promise.resolve();
    await Promise.resolve();

    expect(confirmMock.confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        danger: true,
        impacts: ['12 membre(s)', '34 chant(s)', '5 événement(s)']
      })
    );
    // Confirmation refusée : aucun appel ChangeStatus ne doit partir.
    httpMock.expectNone(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/ChangeStatus`);
  });

  it('409 (transition interdite) : message explicite affiché, pas un code brut', async () => {
    const { component } = createLoaded(fakeDetail({ Status: ChoirStatusEnum.Archived }));
    confirmMock.confirm.mockResolvedValue(true);

    component.statusAction(ChoirStatusEnum.Published)().subscribe({ error: () => undefined });
    await Promise.resolve();
    await Promise.resolve();

    const req = httpMock.expectOne(r => r.url === `${ADMIN_CHOIRS_BASE_URL}/ChangeStatus`);
    req.flush({ Message: 'Transition interdite de Archivée vers Publiée.' }, { status: 409, statusText: 'Conflict' });

    expect(component.error()).toBe('Transition interdite de Archivée vers Publiée.');
  });
});
