import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { SongInstructionsComponent } from './song-instructions.component';
import { environment } from '@env/environment';
import { InstructionStatusEnum } from '@app/enums/status-instruction.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';
import { IInstruction } from '@models/instructions-models/instruction.model';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

const INSTRUCTIONS_BASE_URL = `${environment.apiUrl}instructions`;
const SONG_ID = 'song-1';

function buildInstruction(overrides: Partial<IInstruction> = {}): IInstruction {
  return {
    Id: 'instruction-1',
    SongId: SONG_ID,
    VoicePart: null,
    Title: 'Prononciation',
    Content: 'Attention au latin.',
    Status: InstructionStatusEnum.Draft,
    PublishedAt: null,
    AuthorUserId: 'user-1',
    AuthorName: 'Jean Dupont',
    ...overrides
  };
}

describe('SongInstructionsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(request => request.url.startsWith('/icons/')).forEach(request => request.flush(''));
    httpMock.verify();
  });

  // Assertion explicite plutôt qu'un `!` : si le bouton disparaît du DOM, le test échoue avec
  // un message qui nomme la cause, au lieu d'un TypeError opaque.
  function clickFormSubmit(html: HTMLElement): void {
    const button = html.querySelector<HTMLButtonElement>('form button.btn-primary');
    if (!button) throw new Error('Bouton de soumission absent du DOM');
    button.click();
  }

  function render(instructions: IInstruction[], canManage = true) {
    const fixture = TestBed.createComponent(SongInstructionsComponent);
    fixture.componentRef.setInput('songId', SONG_ID);
    fixture.componentRef.setInput('canManage', canManage);
    fixture.detectChanges();

    httpMock
      .expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`)
      .flush({ Items: instructions, TotalCount: instructions.length, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    return fixture;
  }

  it('charge les consignes du chant reçu en entrée', () => {
    const fixture = render([buildInstruction()]);

    expect(fixture.nativeElement.textContent).toContain('Prononciation');
    expect(fixture.nativeElement.textContent).toContain('Attention au latin.');
  });

  /**
   * `Content` est du texte libre saisi par un utilisateur : il doit être rendu par
   * interpolation, JAMAIS par [innerHTML]. Les retours à la ligne sont restitués par
   * `white-space: pre-wrap` en SCSS, pas par du HTML injecté (OWASP A03).
   */
  it('rend le contenu comme du TEXTE, jamais comme du HTML', () => {
    const fixture = render([buildInstruction({ Content: '<img src=x onerror="alert(1)">' })]);

    const contentEl = fixture.nativeElement.querySelector('.song-instructions__content') as HTMLElement;

    expect(contentEl.textContent).toContain('<img src=x onerror="alert(1)">');
    expect(contentEl.querySelector('img')).toBeNull();
  });

  it('brouillon : Modifier, Publier et Supprimer disponibles, Archiver absent', () => {
    const fixture = render([buildInstruction({ Status: InstructionStatusEnum.Draft })]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Modifier');
    expect(text).toContain('Publier');
    expect(text).not.toContain('Archiver');
  });

  it('publiée : seul Archiver est proposé (ni Modifier ni Publier)', () => {
    const fixture = render([buildInstruction({ Status: InstructionStatusEnum.Published })]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).toContain('Archiver');
    expect(text).not.toContain('Modifier');
    expect(text).not.toContain('Publier');
  });

  // Le back répond 409 sur toute modification d'une consigne archivée : ne pas proposer
  // d'action qui échouerait à coup sûr.
  it('archivée : aucune action de cycle de vie proposée', () => {
    const fixture = render([buildInstruction({ Status: InstructionStatusEnum.Archived })]);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).not.toContain('Modifier');
    expect(text).not.toContain('Publier');
    expect(text).not.toContain('Archiver');
  });

  it('sans droit de gestion : aucune action ni bouton de création', () => {
    const fixture = render([buildInstruction()], false);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

    expect(text).not.toContain('Ajouter une consigne');
    expect(text).not.toContain('Modifier');
    expect(text).not.toContain('Publier');
  });

  it('création : POST Create avec la voix ciblée, puis panneau refermé et liste rechargée', () => {
    const fixture = render([]);
    const component = fixture.componentInstance;

    component.toggleCreateForm();
    fixture.detectChanges();
    component.createForm.setValue({ voicePart: String(VoicePartEnum.Alto), title: 'Altos', content: 'Mesure 12.' });

    clickFormSubmit(fixture.nativeElement);

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/Create`);
    expect(req.request.body).toEqual({
      SongId: SONG_ID,
      VoicePart: VoicePartEnum.Alto,
      Title: 'Altos',
      Content: 'Mesure 12.'
    });
    req.flush(buildInstruction());

    expect(component.showCreateForm()).toBe(false);
    httpMock
      .expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  // « Tout le chœur » = champ ABSENT du corps, pas envoyé à null : le back distingue les deux.
  it('création sans voix ciblée : VoicePart absent du corps', () => {
    const fixture = render([]);
    const component = fixture.componentInstance;

    component.toggleCreateForm();
    fixture.detectChanges();
    component.createForm.setValue({ voicePart: '', title: '', content: 'Pour tout le chœur.' });

    clickFormSubmit(fixture.nativeElement);

    const req = httpMock.expectOne(`${INSTRUCTIONS_BASE_URL}/Create`);
    expect((req.request.body as Record<string, unknown>)['VoicePart']).toBeUndefined();
    expect((req.request.body as Record<string, unknown>)['Title']).toBeUndefined();
    req.flush(buildInstruction());

    httpMock
      .expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`)
      .flush({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 10 });
  });

  /**
   * Non-régression : `load()` lit `page()`, et il était appelé DEPUIS l'effect — l'effect
   * traquait donc `page`. Changer de page l'invalidait, il se rejouait, remettait `page` à 1 et
   * rechargeait : la pagination était morte et chaque clic émettait deux requêtes. `untracked`
   * borne l'effect à sa seule dépendance voulue, `songId`.
   */
  it('pagination : passer à la page 2 émet Page=2 et n\'est pas ramené à 1 par l\'effect', () => {
    const fixture = TestBed.createComponent(SongInstructionsComponent);
    fixture.componentRef.setInput('songId', SONG_ID);
    fixture.componentRef.setInput('canManage', true);
    fixture.detectChanges();

    httpMock
      .expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`)
      .flush({ Items: [buildInstruction()], TotalCount: 25, CurrentPage: 1, PageSize: 10 });
    fixture.detectChanges();

    fixture.componentInstance.goToPage(2);

    const req = httpMock.expectOne(r => r.url === `${INSTRUCTIONS_BASE_URL}/GetPaged`);
    expect(req.request.params.get('Page')).toBe('2');
    req.flush({ Items: [buildInstruction()], TotalCount: 25, CurrentPage: 2, PageSize: 10 });
    fixture.detectChanges();

    expect(fixture.componentInstance.page()).toBe(2);
  });

  it('contenu vide : aucun appel HTTP', () => {
    const fixture = render([]);
    const component = fixture.componentInstance;

    component.toggleCreateForm();
    fixture.detectChanges();
    component.createForm.setValue({ voicePart: '', title: 'Titre seul', content: '' });

    clickFormSubmit(fixture.nativeElement);

    httpMock.expectNone(`${INSTRUCTIONS_BASE_URL}/Create`);
    expect(component.createForm.controls.content.touched).toBe(true);
  });
});
