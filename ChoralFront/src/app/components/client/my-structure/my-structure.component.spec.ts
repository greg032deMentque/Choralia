import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { MyStructureComponent } from './my-structure.component';
import { environment } from '@env/environment';
import { IClient } from '@models/admin-models/client.model';
import { IClientManagerListItem } from '@models/admin-models/client-manager-list-item.model';
import { ClientStatusEnum } from '@app/enums/status-client.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const CLIENTS_BASE_URL = `${environment.apiUrl}clients`;
const STRUCTURE_ID = '22222222-2222-2222-2222-222222222222';

function fakeStructure(overrides: Partial<IClient> = {}): IClient {
  return {
    Id: STRUCTURE_ID,
    Name: 'Diocèse de Test',
    ContactName: null,
    ContactEmail: null,
    Status: ClientStatusEnum.Active,
    ChoirLimit: 10,
    MemberLimit: 100,
    StorageQuotaBytes: 1000,
    MaxFileSizeBytes: 100,
    ChoirCount: 2,
    MemberCount: 20,
    UsedStorageBytes: 500,
    ...overrides
  };
}

function fakeManager(overrides: Partial<IClientManagerListItem> = {}): IClientManagerListItem {
  return {
    UserId: 'user-1',
    Firstname: 'Jeanne',
    Lastname: 'Dupont',
    Email: 'jeanne.dupont@exemple.fr',
    // Toujours ClientManager en pratique côté back sur cette route — le cas qui a fait fuiter
    // « Responsable client » (getUserRoleLabel) avant le fix.
    Role: UserRoleEnum.ClientManager,
    AssignmentDate: '2026-01-15T00:00:00Z',
    ...overrides
  };
}

describe('MaStructureComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => req.flush(null));
    httpMock.verify();
  });

  function createLoaded(structure: IClient) {
    const fixture = TestBed.createComponent(MyStructureComponent);
    fixture.componentRef.setInput('clientId', structure.Id);
    fixture.detectChanges();
    httpMock.expectOne(`${CLIENTS_BASE_URL}/${structure.Id}`).flush(structure);
    httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/${structure.Id}/GetChoirs`).flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance };
  }

  it('les plafonds sont en lecture seule : aucun contrôle de saisie rendu dans l’onglet Plafonds', () => {
    const { fixture, component } = createLoaded(fakeStructure());
    component.selectTab('limits');
    fixture.detectChanges();

    const inputs = fixture.nativeElement.querySelectorAll('input, textarea, select');
    expect(inputs.length).toBe(0);
  });

  it('le mot « client » n’apparaît dans aucun texte rendu de cette zone', () => {
    const { fixture, component } = createLoaded(fakeStructure());

    for (const tab of ['choirs', 'limits', 'managers'] as const) {
      component.selectTab(tab);
      fixture.detectChanges();

      if (tab === 'managers') {
        // Flush un responsable réel (pas une liste vide) : sinon aucune ligne n'est rendue et
        // ce test ne peut pas détecter une fuite du mot « client » dans la cellule Rôle (bug
        // constaté : getUserRoleLabel(UserRoleEnum.ClientManager) rendait « Responsable client »).
        httpMock
          .expectOne(r => r.url === `${CLIENTS_BASE_URL}/${STRUCTURE_ID}/Managers`)
          .flush({ Items: [fakeManager()], TotalCount: 1, CurrentPage: 1, PageSize: 10 });
        fixture.detectChanges();
      }

      const text = (fixture.nativeElement.textContent as string).toLowerCase();
      expect(text).not.toContain('client');
    }
  });

  // Garde la même règle de recette que le test précédent, sur le contenu ajouté par ce lot
  // (modale de création de chorale) — risque de dérive déjà constaté sur ce projet.
  it('le mot « client » n’apparaît pas dans la modale de création de chorale', () => {
    const { fixture, component } = createLoaded(fakeStructure());

    component.openCreateChoirForm();
    fixture.detectChanges();

    const text = (fixture.nativeElement.textContent as string).toLowerCase();
    expect(text).not.toContain('client');
  });

  it('la désignation d’un responsable recharge la liste des responsables', () => {
    const { fixture, component } = createLoaded(fakeStructure());
    component.selectTab('managers');
    fixture.detectChanges();
    httpMock
      .expectOne(r => r.url === `${CLIENTS_BASE_URL}/${STRUCTURE_ID}/Managers`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    component.assignForm.setValue({ email: 'chef@exemple.fr' });
    component.assignManager().subscribe();

    httpMock.expectOne(`${CLIENTS_BASE_URL}/${STRUCTURE_ID}/Managers`).flush(null);

    const reload = httpMock.expectOne(r => r.url === `${CLIENTS_BASE_URL}/${STRUCTURE_ID}/Managers`);
    expect(reload.request.method).toBe('GET');
    reload.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });
});
