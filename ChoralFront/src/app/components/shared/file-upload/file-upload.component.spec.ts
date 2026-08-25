import { TestBed } from '@angular/core/testing';
import { FileUploadComponent } from './file-upload.component';

function buildFile(name: string, sizeBytes: number, type = 'application/octet-stream'): File {
  return new File([new Uint8Array(sizeBytes)], name, { type });
}

function buildInputChangeEvent(file: File): Event {
  const input = document.createElement('input');
  input.type = 'file';
  Object.defineProperty(input, 'files', { value: [file], writable: false });
  return { target: input } as unknown as Event;
}

// handleFile (privée) valide extension + taille avant émission — testée via le point d'entrée
// public onFileInputChange, réutilisé par ScoreUploadFormComponent et
// RecordingUploadFormComponent.
describe('FileUploadComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  function createComponent(accept: string, maxSizeMo: number): FileUploadComponent {
    const fixture = TestBed.createComponent(FileUploadComponent);
    fixture.componentRef.setInput('accept', accept);
    fixture.componentRef.setInput('maxSizeMo', maxSizeMo);
    return fixture.componentInstance;
  }

  it('émet fileSelected pour un fichier valide (extension et taille conformes)', () => {
    const component = createComponent('.pdf,.png,.jpg,.jpeg', 20);
    const fileSelected = vi.fn();
    const validationError = vi.fn();
    component.fileSelected.subscribe(fileSelected);
    component.validationError.subscribe(validationError);

    const file = buildFile('score.pdf', 1024, 'application/pdf');
    component.onFileInputChange(buildInputChangeEvent(file));

    expect(fileSelected).toHaveBeenCalledWith(file);
    expect(validationError).toHaveBeenCalledWith(null);
  });

  it("refuse un fichier dont l'extension ne fait pas partie des formats acceptés", () => {
    const component = createComponent('.pdf,.png,.jpg,.jpeg', 20);
    const fileSelected = vi.fn();
    const validationError = vi.fn();
    component.fileSelected.subscribe(fileSelected);
    component.validationError.subscribe(validationError);

    const file = buildFile('notes.txt', 1024, 'text/plain');
    component.onFileInputChange(buildInputChangeEvent(file));

    expect(fileSelected).not.toHaveBeenCalled();
    expect(validationError).toHaveBeenCalledWith('Format non autorisé. Formats acceptés : .pdf,.png,.jpg,.jpeg');
  });

  it('refuse un fichier dépassant la taille maximale autorisée', () => {
    const component = createComponent('.pdf', 1);
    const fileSelected = vi.fn();
    const validationError = vi.fn();
    component.fileSelected.subscribe(fileSelected);
    component.validationError.subscribe(validationError);

    const file = buildFile('gros-fichier.pdf', 2 * 1024 * 1024, 'application/pdf');
    component.onFileInputChange(buildInputChangeEvent(file));

    expect(fileSelected).not.toHaveBeenCalled();
    expect(validationError).toHaveBeenCalledWith('Fichier trop volumineux (max 1 Mo).');
  });

  it('accepte une extension en majuscules grâce à la comparaison insensible à la casse', () => {
    const component = createComponent('.pdf', 20);
    const fileSelected = vi.fn();
    component.fileSelected.subscribe(fileSelected);

    const file = buildFile('Score.PDF', 1024, 'application/pdf');
    component.onFileInputChange(buildInputChangeEvent(file));

    expect(fileSelected).toHaveBeenCalledWith(file);
  });
});
