import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { AdminDashboardComponent } from './dashboard.component';
import { ConfirmService } from '@app/services/confirm.service';
import { environment } from '@env/environment';
import { RoutePaths } from '@core/route-paths';
import { ClientStatusEnum } from '@app/enums/status-client.enum';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';
import { IAdminDashboardKpi } from '@models/admin-models/admin-dashboard-kpi.model';
import { formatBytes } from '@app/services/admin/format-bytes.util';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const ADMIN_DASHBOARD_BASE_URL = `${environment.apiUrl}admin-dashboard`;
const ADMIN_GUEST_ACCOUNTS_BASE_URL = `${environment.apiUrl}admin-guest-accounts`;

function fakeKpi(overrides: Partial<IAdminDashboardKpi> = {}): IAdminDashboardKpi {
  return {
    Clients: { Total: 10, Active: 8, Suspended: 0, Archived: 2 },
    Choirs: { Total: 5, Draft: 1, Published: 3, Cancelled: 0, Archived: 1 },
    Users: { Total: 20, Active: 18, InactiveInvitees: 2 },
    InactiveChoirs: { Count: 1, ChoirIds: ['chorale-1'] },
    NotStartedClients: { Count: 0, ClientIds: [] },
    ClientsNearCap: { Count: 2, ClientIds: ['client-1', 'client-2'] },
    TotalStorageBytes: 2 * 1024 * 1024,
    Songs: { Total: 100, DuplicateGroups: 4 },
    UpcomingEvents30Days: 6,
    EventsWithoutStructureAnomaly: { Count: 0, EventIds: [] },
    ...overrides
  };
}

describe('AdminDashboardComponent', () => {
  let httpMock: HttpTestingController;
  let router: Router;
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
    router = TestBed.inject(Router);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush(null));
    httpMock.verify();
  });

  function findTile(component: AdminDashboardComponent, key: string) {
    for (const section of component.sections()) {
      const tile = section.tiles.find(t => t.key === key);
      if (tile) return tile;
    }
    return undefined;
  }

  it('clic sur la tuile "Clients actifs" : navigue vers /admin/clients filtré par Statut=Actif', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    const tile = findTile(fixture.componentInstance, 'clients-actifs');
    expect(tile?.clickable).toBe(true);
    tile?.onClick?.();

    expect(navigateSpy).toHaveBeenCalledWith(
      ['/', RoutePaths.Admin, RoutePaths.AdminClients],
      { queryParams: { Status: ClientStatusEnum.Active } }
    );
  });

  it('clic sur la tuile "Chorales inactives depuis 30 jours" : navigue avec InactiveDepuis30Jours=true', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    const tile = findTile(fixture.componentInstance, 'choirs-inactives');
    expect(tile?.clickable).toBe(true);
    tile?.onClick?.();

    expect(navigateSpy).toHaveBeenCalledWith(
      ['/', RoutePaths.Admin, RoutePaths.AdminChoirs],
      { queryParams: { InactiveFor30Days: true } }
    );
  });

  it('clic sur la tuile "Chorales publiées" : navigue avec Statut=Publie (deuxième nature de tuile)', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const navigateSpy = vi.spyOn(router, 'navigate').mockResolvedValue(true);

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    const tile = findTile(fixture.componentInstance, 'choirs-publiees');
    tile?.onClick?.();

    expect(navigateSpy).toHaveBeenCalledWith(
      ['/', RoutePaths.Admin, RoutePaths.AdminChoirs],
      { queryParams: { Status: ChoirStatusEnum.Published } }
    );
  });

  it('tuile à 0 (Clients suspendus) : non cliquable', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi({ Clients: { Total: 10, Active: 8, Suspended: 0, Archived: 2 } }));

    const tile = findTile(fixture.componentInstance, 'clients-suspendus');
    expect(tile?.value).toBe(0);
    expect(tile?.clickable).toBe(false);
  });

  it("stockage total : affiché via formatBytes, jamais l'octet brut", () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const octets = 2 * 1024 * 1024;
    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi({ TotalStorageBytes: octets }));

    const tile = fixture.componentInstance.storageTile();
    expect(tile?.displayValue).toBe(formatBytes(octets));
    expect(tile?.displayValue).not.toBe(String(octets));
    expect(tile?.clickable).toBe(false);
  });

  it('GetKpi en erreur : le bloc de purge reste utilisable (chargement partiel)', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.kpiError()).toBeTruthy();
    expect(component.sections()).toEqual([]);

    component.previewPurge();
    httpMock
      .expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`)
      .flush({ Count: 3, HasMore: false, Candidates: [] });

    expect(component.purgePreviewError()).toBeNull();
    expect(component.purgePreview()?.Count).toBe(3);
  });

  it('Réessayer relance GetKpi après une erreur HTTP', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const retryButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.data-state--error button'
    );
    expect(retryButton?.textContent?.trim()).toBe('Réessayer');
    retryButton?.click();

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());
    expect(fixture.componentInstance.kpi()).toEqual(fakeKpi());
  });

  it('Aperçu de purge en erreur : les tuiles KPI déjà chargées restent affichées', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());
    expect(component.sections().length).toBeGreaterThan(0);

    component.previewPurge();
    httpMock.expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`).flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.purgePreviewError()).toBeTruthy();
    // Le KPI, chargé avant l'échec de la purge, n'est pas affecté.
    expect(component.sections().length).toBeGreaterThan(0);
    expect(component.kpiError()).toBeNull();
  });

  it('Purge : le nombre affiché après action est celui retourné par PurgeInactive, pas celui de l’aperçu', async () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    component.previewPurge();
    httpMock
      .expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`)
      .flush({ Count: 5, HasMore: false, Candidates: [] });
    expect(component.purgePreview()?.Count).toBe(5);

    confirmMock.confirm.mockResolvedValue(true);
    component.purgeAction().subscribe();
    await Promise.resolve();
    await Promise.resolve();

    httpMock.expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/PurgeInactive`).flush({ AnonymizedCount: 4, HasMore: false });

    expect(component.purgeResult()).toBe(4);
  });

  it('Purge : aperçu seul ne déclenche aucun appel PurgeInactive', () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    component.previewPurge();
    httpMock
      .expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`)
      .flush({ Count: 2, HasMore: false, Candidates: [] });

    httpMock.expectNone(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/PurgeInactive`);
  });

  it('Purge : confirmation annulée -> aucun appel PurgeInactive', async () => {
    const fixture = TestBed.createComponent(AdminDashboardComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(`${ADMIN_DASHBOARD_BASE_URL}/GetKpi`).flush(fakeKpi());

    component.previewPurge();
    httpMock
      .expectOne(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/GetPurgeCandidates`)
      .flush({ Count: 2, HasMore: false, Candidates: [] });

    confirmMock.confirm.mockResolvedValue(false);
    component.purgeAction().subscribe({ error: () => undefined });
    await Promise.resolve();
    await Promise.resolve();

    httpMock.expectNone(`${ADMIN_GUEST_ACCOUNTS_BASE_URL}/PurgeInactive`);
    expect(component.purgeResult()).toBeNull();
  });
});
