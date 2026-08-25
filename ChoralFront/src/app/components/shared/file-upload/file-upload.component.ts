import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Composant générique drag&drop + file input — réutilisé par ScoreUploadFormComponent
// (pdf/png/jpg/jpeg, 20 Mo) et RecordingUploadFormComponent (mp3/m4a/wav, 100 Mo).
// Validation extension + taille faite ici avant émission du fichier — le back revalide de
// toute façon (400 format rejeté, 413 trop volumineux).
@Component({
  selector: 'app-file-upload',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './file-upload.component.html',
  styleUrl: './file-upload.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FileUploadComponent {
  readonly accept = input.required<string>();
  readonly maxSizeMo = input.required<number>();
  readonly disabled = input<boolean>(false);
  readonly label = input<string>('Glissez-déposez un fichier ou cliquez pour parcourir');

  readonly fileSelected = output<File>();
  readonly validationError = output<string | null>();
  /**
   * Retrait explicite du fichier sélectionné. Indispensable et pas seulement confortable :
   * ce composant ne détient que le NOM du fichier, l'objet `File` vit chez le parent. Effacer
   * ici sans le dire laissait le parent avec son fichier — donc un fichier « retiré » à
   * l'écran mais toujours soumis au dépôt.
   *
   * `output()` sans paramètre de type plutôt que `output<void>()` : même signature d'emit,
   * sans déclencher `no-invalid-void-type` (convention déjà appliquée à `loadFailed` de
   * SongPickerComponent).
   */
  readonly cleared = output();

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly isDragging = signal(false);
  protected readonly selectedFileName = signal<string | null>(null);

  protected readonly acceptedExtensions = computed(() => this.accept().split(',').map(ext => ext.trim().toLowerCase()));

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    if (!this.disabled()) {
      this.isDragging.set(true);
    }
  }

  onDragLeave(): void {
    this.isDragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
    if (this.disabled()) return;
    const file = event.dataTransfer?.files?.[0];
    if (file) this.handleFile(file);
  }

  onFileInputChange(event: Event): void {
    const target = event.target as HTMLInputElement;
    const file = target.files?.[0];
    if (file) this.handleFile(file);
    target.value = '';
  }

  clear(): void {
    this.selectedFileName.set(null);
    this.cleared.emit();
  }

  private handleFile(file: File): void {
    const extension = `.${file.name.split('.').pop()?.toLowerCase() ?? ''}`;
    if (!this.acceptedExtensions().includes(extension)) {
      this.validationError.emit(`Format non autorisé. Formats acceptés : ${this.accept()}`);
      return;
    }

    const maxBytes = this.maxSizeMo() * 1024 * 1024;
    if (file.size > maxBytes) {
      this.validationError.emit(`Fichier trop volumineux (max ${this.maxSizeMo()} Mo).`);
      return;
    }

    this.validationError.emit(null);
    this.selectedFileName.set(file.name);
    this.fileSelected.emit(file);
  }
}
