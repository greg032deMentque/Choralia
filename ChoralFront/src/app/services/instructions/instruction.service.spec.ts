import { TestBed } from '@angular/core/testing';
import { HttpRequest, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { InstructionService } from './instruction.service';
import { environment } from '@env/environment';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

const INSTRUCTIONS_BASE_URL = `${environment.apiUrl}instructions`;
const SONG_ID = 'song-1';
const INSTRUCTION_ID = 'instruction-1';

/**
 * InstructionController mélange trois conventions de route (identifiant dans le corps pour
 * Update, dans le chemin pour Publish/Archive, en query param pour Delete). Un service écrit
 * « par habitude » produirait des 404/405 silencieux : ce fichier verrouille chaque URL et
 * chaque verbe.
 */
describe('InstructionService', () => {
  let service: InstructionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(InstructionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getPaged interroge POST /GetPaged avec SongId en query param', () => {
    service.getPaged({ SongId: SONG_ID }, { Page: 2, PageSize: 10 }).subscribe();

    const req = httpMock.expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`);
    expect(req.request.method).toBe('POST');
    expect(req.request.params.get('SongId')).toBe(SONG_ID);
    expect(req.request.params.get('Page')).toBe('2');
    req.flush({ Items: [], TotalCount: 0, CurrentPage: 2, PageSize: 10 });
  });

  it('create envoie SongId et la voix ciblée dans le corps', () => {
    service.create({ SongId: SONG_ID, VoicePart: VoicePartEnum.Alto, Content: 'Contenu' }).subscribe();

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/Create`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ SongId: SONG_ID, VoicePart: VoicePartEnum.Alto, Content: 'Contenu' });
    req.flush({});
  });

  // Une consigne sans voix ciblée s'adresse à tout le chœur : le champ doit être ABSENT du
  // corps, pas envoyé à null — le back distingue les deux.
  it('create omet VoicePart quand la consigne vise tout le chœur', () => {
    service.create({ SongId: SONG_ID, Content: 'Contenu' }).subscribe();

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/Create`);
    expect((req.request.body as Record<string, unknown>)['VoicePart']).toBeUndefined();
    req.flush({});
  });

  it('update porte l\'identifiant dans le CORPS, jamais en query param', () => {
    service.update({ Id: INSTRUCTION_ID, Content: 'Nouveau contenu' }).subscribe();

    const req: HttpRequest<unknown> = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/Update`).request;
    expect(req.method).toBe('PUT');
    expect(req.body).toEqual({ Id: INSTRUCTION_ID, Content: 'Nouveau contenu' });
    expect(req.params.keys()).toHaveLength(0);
    httpMock.verify();
  });

  it('publish porte l\'identifiant dans le CHEMIN', () => {
    service.publish(INSTRUCTION_ID).subscribe();

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/${INSTRUCTION_ID}/Publish`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('archive porte l\'identifiant dans le CHEMIN', () => {
    service.archive(INSTRUCTION_ID).subscribe();

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/${INSTRUCTION_ID}/Archive`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('delete porte l\'identifiant en QUERY PARAM', () => {
    service.delete(INSTRUCTION_ID).subscribe();

    const req = httpMock.expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/Delete`);
    expect(req.request.method).toBe('DELETE');
    expect(req.request.params.get('id')).toBe(INSTRUCTION_ID);
    req.flush(null);
  });
});
