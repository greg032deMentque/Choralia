import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { registerLocaleData } from '@angular/common';
import localeFr from '@angular/common/locales/fr';
import { AdminAuditComponent } from './audit.component';
import { environment } from '@env/environment';
import { IAdminAuditLogListItem } from '@models/admin-models/admin-audit-log.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

// registerLocaleData + LOCALE_ID='fr-FR' : reproduit ici ce qui est fourni globalement par
// app.config.ts (non chargé par ce TestBed isolé) — sans les deux, le pipe `date` retomberait
// sur en-US par défaut et le test ci-dessous passerait pour de mauvaises raisons.
registerLocaleData(localeFr);

const ADMIN_AUDIT_BASE_URL = `${environment.apiUrl}admin-audit`;

const FAKE_ROW: IAdminAuditLogListItem = {
  Id: 'log-1',
  UserId: 'user-supprime',
  UserFullName: 'Utilisateur inconnu',
  UserEmail: null,
  Action: 'ChangerStatut',
  EntityType: 'Chorale',
  EntityId: 'chorale-1',
  Detail: null,
  OccurredAt: '2026-07-01T10:00:00Z'
};

describe('AdminAuditComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: LOCALE_ID, useValue: 'fr-FR' }]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 }));
    httpMock.verify();
  });

  it('filtre de période : transmet DateDebut/DateFin en bornes de journée (00:00:00 / 23:59:59)', () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });

    component.onStartDateChange('2026-07-01');
    httpMock
      .expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });

    component.onEndDateChange('2026-07-31');

    const req = httpMock.expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`);
    expect(req.request.params.get('StartDate')).toBe('2026-07-01T00:00:00');
    expect(req.request.params.get('EndDate')).toBe('2026-07-31T23:59:59');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });
  });

  it('période inversée : message dédié affiché, distinct du message "aucun résultat", et AUCUN appel réseau', () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);
    const component = fixture.componentInstance;

    httpMock.expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });

    component.onStartDateChange('2026-07-31');
    httpMock
      .expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });

    component.onEndDateChange('2026-07-01');

    httpMock.expectNone(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`);
    expect(component.error()).toBe('Période invalide : la date de début est postérieure à la date de fin.');
    expect(component.error()).not.toBe('Aucun résultat pour ce filtre.');
    expect(component.items()).toEqual([]);
  });

  it("acteur supprimé : la ligne reste affichée avec 'Utilisateur inconnu', jamais masquée", async () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);

    httpMock
      .expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`)
      .flush({ Items: [FAKE_ROW], TotalCount: 1, CurrentPage: 1, PageSize: 20 });

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Utilisateur inconnu');
    expect(fixture.componentInstance.items().length).toBe(1);
  });

  it('OccurredAt est rendu au format français jour/mois/année, jamais au format américain mois/jour', async () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);

    httpMock
      .expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`)
      .flush({ Items: [FAKE_ROW], TotalCount: 1, CurrentPage: 1, PageSize: 20 });

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    // FAKE_ROW.OccurredAt = '2026-07-01T10:00:00Z' -> 1er juillet 2026 (jamais lu comme le
    // 7e mois du jour 1 à l'américaine, ni tronqué en année sur 2 chiffres).
    expect(text).toContain('01/07/2026');
    expect(text).not.toContain('7/1/26');
  });

  it('seules OccurredAt, Action, EntityType sont déclarées triables', () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);
    httpMock.expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 });

    const sortableKeys = fixture.componentInstance.columns().filter(c => c.sortable).map(c => c.key);
    expect(sortableKeys.sort()).toEqual(['Action', 'EntityType', 'OccurredAt'].sort());
  });

  it('aucune action d’écriture rendue (pas de bouton modifier/supprimer)', async () => {
    const fixture = TestBed.createComponent(AdminAuditComponent);

    httpMock
      .expectOne(r => r.url === `${ADMIN_AUDIT_BASE_URL}/GetPaged`)
      .flush({ Items: [FAKE_ROW], TotalCount: 1, CurrentPage: 1, PageSize: 20 });

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
    const writeButtons = buttons.filter(btn => /update|delete|éditer|delete|edit/i.test(btn.textContent ?? ''));
    expect(writeButtons.length).toBe(0);
  });
});
