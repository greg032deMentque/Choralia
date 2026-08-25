import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { SongFormComponent } from './song-form.component';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// voicePartMinOneValidator exige au moins une voix sélectionnée ; toggleVoicePart ajoute/retire une
// voix de la sélection. Le composant n'est jamais rendu (pas de detectChanges) : ces tests
// exercent uniquement le FormGroup et les méthodes publiques, sans dépendre du template.
describe('SongFormComponent', () => {
  let component: SongFormComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    const fixture = TestBed.createComponent(SongFormComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('voiceParts vide : le contrôle est invalide (voicePartMinOneValidator)', () => {
    expect(component.form.controls.voiceParts.value).toEqual([]);
    expect(component.form.controls.voiceParts.invalid).toBe(true);
    expect(component.form.controls.voiceParts.errors).toEqual({ voicePartRequired: true });
  });

  it('voiceParts non vide : le contrôle est valide (voicePartMinOneValidator)', () => {
    component.toggleVoicePart(VoicePartEnum.Alto);

    expect(component.form.controls.voiceParts.value).toEqual([VoicePartEnum.Alto]);
    expect(component.form.controls.voiceParts.valid).toBe(true);
  });

  it('toggleVoicePart ajoute une voix absente à la sélection', () => {
    component.toggleVoicePart(VoicePartEnum.Soprano);

    expect(component.form.controls.voiceParts.value).toEqual([VoicePartEnum.Soprano]);
  });

  it('toggleVoicePart retire une voix déjà sélectionnée', () => {
    component.toggleVoicePart(VoicePartEnum.Soprano);
    component.toggleVoicePart(VoicePartEnum.Soprano);

    expect(component.form.controls.voiceParts.value).toEqual([]);
  });
});
