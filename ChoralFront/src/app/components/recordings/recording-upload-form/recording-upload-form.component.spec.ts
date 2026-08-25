import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RecordingUploadFormComponent } from './recording-upload-form.component';
import { RecordingService } from '@app/services/recordings/recording.service';
import { RecordingTypeEnum } from '@app/enums/type-recording.enum';
import { VoicePartEnum } from '@app/enums/voice-part.enum';

// jsdom ne décode pas l'audio nativement (pas de moteur média) : measureDuration() instancie
// `new Audio()` en production, on stubbe le global Audio par un faux élément pilotable
// synchronement, sans update le code de production.
class FakeAudioElementSuccess {
  preload = '';
  duration = 125.4;
  private readonly listeners = new Map<string, (() => void)[]>();

  addEventListener(type: string, callback: () => void): void {
    const existing = this.listeners.get(type) ?? [];
    existing.push(callback);
    this.listeners.set(type, existing);
  }

  removeEventListener(type: string, callback: () => void): void {
    const remaining = (this.listeners.get(type) ?? []).filter(listener => listener !== callback);
    this.listeners.set(type, remaining);
  }

  set src(_value: string) {
    (this.listeners.get('loadedmetadata') ?? []).forEach(callback => callback());
  }
}

class FakeAudioElementError {
  preload = '';
  private readonly listeners = new Map<string, (() => void)[]>();

  addEventListener(type: string, callback: () => void): void {
    const existing = this.listeners.get(type) ?? [];
    existing.push(callback);
    this.listeners.set(type, existing);
  }

  removeEventListener(type: string, callback: () => void): void {
    const remaining = (this.listeners.get(type) ?? []).filter(listener => listener !== callback);
    this.listeners.set(type, remaining);
  }

  set src(_value: string) {
    (this.listeners.get('error') ?? []).forEach(callback => callback());
  }
}

function buildFile(name = 'test.mp3'): File {
  return new File([new Uint8Array(10)], name, { type: 'audio/mpeg' });
}

describe('RecordingUploadFormComponent', () => {
  let component: RecordingUploadFormComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    const fixture = TestBed.createComponent(RecordingUploadFormComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    URL.createObjectURL = vi.fn(() => 'blob:mock-url');
    URL.revokeObjectURL = vi.fn();
  });

  afterEach(() => {
    httpMock.verify();
    vi.unstubAllGlobals();
  });

  it('mesure la durée via loadedmetadata et met à jour durationSecondes (measureDuration)', () => {
    vi.stubGlobal('Audio', FakeAudioElementSuccess);

    component.onFileSelected(buildFile());

    expect(component.durationSecondes()).toBe(125);
    expect(component.fileError()).toBeNull();
  });

  it('réinitialise le fichier sélectionné si la lecture de la durée échoue (measureDuration)', () => {
    vi.stubGlobal('Audio', FakeAudioElementError);

    component.onFileSelected(buildFile());

    expect(component.fileError()).toBe('Impossible de lire la durée de ce fichier audio.');
    expect(component.selectedFile()).toBeNull();
    expect(component.durationSecondes()).toBeNull();
  });

  it("bloque la soumission tant que la durée n'a pas été mesurée (submit)", () => {
    const service = TestBed.inject(RecordingService);
    const createSpy = vi.spyOn(service, 'create');

    component.form.controls.songId.setValue('chant-1');
    component.form.controls.ownerContent.setValue('Jean Dupont');
    component.selectedFile.set(buildFile());

    component.submit();

    expect(component.error()).toBe("La durée du fichier n'a pas pu être mesurée. Merci de resélectionner le fichier.");
    expect(createSpy).not.toHaveBeenCalled();
  });

  it('onTypeChange remet targetVoicePart à null en repassant au type Général', () => {
    component.onTypeChange(String(RecordingTypeEnum.ByVoicePart));
    component.form.controls.targetVoicePart.setValue(VoicePartEnum.Alto);

    component.onTypeChange(String(RecordingTypeEnum.General));

    expect(component.form.controls.targetVoicePart.value).toBeNull();
    expect(component.selectedType()).toBe(RecordingTypeEnum.General);
  });
});
