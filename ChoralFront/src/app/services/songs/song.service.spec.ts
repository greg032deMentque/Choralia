import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SongService } from './song.service';
import { environment } from '@env/environment';
import { SongStatusEnum } from '@app/enums/song-status.enum';
import { SongPriorityEnum } from '@app/enums/priority-song.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

const SONGS_BASE_URL = `${environment.apiUrl}songs`;

describe('SongService', () => {
  let service: SongService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(SongService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  /**
   * Non-régression : les quatre filtres partaient sous leurs anciens noms français
   * (ChoraleId, Voix, Statut, Priorite) alors que SongPagedFilterViewModel attend ChoirId,
   * VoicePart, Status et Priority. Le model binding ASP.NET ignore une clé inconnue en
   * silence : l'écran Chants affichait donc le répertoire de TOUTES les chorales accessibles,
   * filtres inopérants et chips de filtre mensongères, sans aucun signal d'erreur.
   */
  it('getPaged transmet les filtres sous les noms exacts de SongPagedFilterViewModel', () => {
    service
      .getPaged(
        {
          ChoirId: 'choir-1',
          VoicePart: VoicePartEnum.Soprano,
          Status: SongStatusEnum.Active,
          Priority: SongPriorityEnum.High
        },
        { Page: 1, PageSize: 25 }
      )
      .subscribe();

    const req = httpMock.expectOne(r => r.url === `${SONGS_BASE_URL}/GetPaged`);
    const params = req.request.params;

    expect(params.get('ChoirId')).toBe('choir-1');
    expect(params.get('VoicePart')).toBe(String(VoicePartEnum.Soprano));
    expect(params.get('Status')).toBe(String(SongStatusEnum.Active));
    expect(params.get('Priority')).toBe(String(SongPriorityEnum.High));
    expect(params.keys()).not.toContain('ChoraleId');
    expect(params.keys()).not.toContain('Voix');
    expect(params.keys()).not.toContain('Statut');
    expect(params.keys()).not.toContain('Priorite');

    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 25 });
  });

  it('getPaged omet les filtres non renseignés', () => {
    service.getPaged({}, { Page: 1, PageSize: 25 }).subscribe();

    const req = httpMock.expectOne(r => r.url === `${SONGS_BASE_URL}/GetPaged`);
    expect(req.request.params.keys().sort()).toEqual(['Page', 'PageSize']);
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 25 });
  });

  /**
   * `PaginateViewModel.PageSize` porte `[Range(1, 100)]` : au-delà, le back répond 400. C'est ce
   * qui rendait le sélecteur de chant vide sur Partitions et Enregistrements, et donc le bouton
   * « Ajouter » invisible (il exige un chant sélectionné).
   *
   * Le plafond ne vit plus que dans `getChoirOptions` — la valeur fautive était dupliquée dans
   * cinq composants. C'est donc ICI qu'il doit être verrouillé : ce test échoue si quelqu'un
   * remonte la constante, où qu'il croie pouvoir le faire.
   */
  it('getChoirOptions ne dépasse jamais le plafond de PageSize accepté par le back', () => {
    service.getChoirOptions('choir-1').subscribe();

    const req = httpMock.expectOne(r => r.url === `${SONGS_BASE_URL}/GetPagedByChoir`);
    expect(Number(req.request.params.get('PageSize'))).toBeLessThanOrEqual(100);
    expect(req.request.params.get('ChoirId')).toBe('choir-1');
    expect(req.request.params.get('SortActive')).toBe('Title');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 100 });
  });

  // Le mapping vers ISelectOption vit dans le service, pas dans chaque appelant : un chant sans
  // Id (jamais renvoyé par le back en pratique) ne doit pas produire `undefined` en valeur
  // d'option, ce qui casserait silencieusement la sélection.
  it('getChoirOptions projette titre et identifiant en options de sélecteur', async () => {
    const optionsPromise = firstValueFrom(service.getChoirOptions('choir-1'));

    httpMock.expectOne(r => r.url === `${SONGS_BASE_URL}/GetPagedByChoir`).flush({
      Items: [
        { Id: 'song-1', Title: 'Alléluia' },
        { Id: null, Title: 'Sans identifiant' }
      ],
      TotalCount: 2,
      CurrentPage: 1,
      PageSize: 100
    });

    expect(await optionsPromise).toEqual([
      { Value: 'song-1', Label: 'Alléluia' },
      { Value: '', Label: 'Sans identifiant' }
    ]);
  });
});
