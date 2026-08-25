import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { EventFormComponent } from './event-form.component';
import { AuthStore } from '@core/auth.store';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { EventTypeEnum } from '@app/enums/event-type.enum';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventEffectiveStateEnum } from '@app/enums/event-effective-state.enum';
import { IEvent } from '@models/events-models/event.model';
import { environment } from '@env/environment';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';

const EVENTS_BASE_URL = `${environment.apiUrl}events`;

function buildUser(): IAuthenticatedUser {
  return {
    Id: 'user-1',
    Email: 'responsable@choralehelper.fr',
    Firstname: 'Jean',
    Lastname: 'Dupont',
    Roles: [],
    SpaceRoles: [
      { SpaceId: 'chorale-1', Name: 'Chorale Sainte-Cécile', SpaceType: SpaceTypeEnum.Choir, Roles: ['Manager'], ClientId: null, ChoirId: null, PrimaryVoicePart: null }
    ],
    ClientRoles: []
  };
}

// Ce formulaire vit exclusivement dans la zone /management (espace chorale actif) : ChoirId est
// toujours transmis depuis AuthStore.activeSpaceId, jamais null. ClientId (ajouté à IEvent
// en correction ciblée — le champ n'existait pas avant, l'oublier ici l'aurait silencieusement
// omis du payload JSON) ne s'applique qu'aux événements autonomes ; on vérifie qu'il est bien
// transmis (null en création) plutôt que silencieusement absent.
describe('EventFormComponent — payload ChoirId/ClientId', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    TestBed.inject(AuthStore).setCurrentUser(buildUser());
    TestBed.inject(AuthStore).setActiveSpace('choir-1');
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('création : le payload envoyé inclut ChoirId (espace actif) et ClientId: null', () => {
    const fixture = TestBed.createComponent(EventFormComponent);
    fixture.detectChanges();

    fixture.componentInstance.form.setValue({
      title: 'Concert de Noël',
      description: null,
      dateDebut: '2026-12-24T18:00',
      dateFin: null,
      type: EventTypeEnum.Concert,
      location: 'Église Saint-Martin'
    });
    fixture.componentInstance.submit();

    const req = httpMock.expectOne(`${EVENTS_BASE_URL}/Create`);
    expect(req.request.body.ChoirId).toBe('choir-1');
    expect(req.request.body.ClientId).toBeNull();

    req.flush({
      Id: 'evt-1',
      Title: 'Concert de Noël',
      Description: null,
      StartDate: '2026-12-24T18:00:00.000Z',
      EndDate: null,
      Type: EventTypeEnum.Concert,
      Location: 'Église Saint-Martin',
      Status: EventStatusEnum.Draft,
      EffectiveState: EventEffectiveStateEnum.Draft,
      ChoirId: 'choir-1',
      ClientId: null,
      ClosedAt: null
    } satisfies IEvent);
  });
});
