import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RecordingService } from '@app/services/recordings/recording.service';
import { FileUploadComponent } from '@app/components/shared/file-upload/file-upload.component';
import { SongSelectComponent } from '@app/components/shared/song-select/song-select.component';
import { IRecording } from '@models/recordings-models/recording.model';
import { ICreateRecordingRequest } from '@models/recordings-models/create-recording-request.model';
import { RecordingTypeEnum, getTypeRecordingLabel } from '@app/enums/type-recording.enum';
import { RecordingSourceEnum, getSourceRecordingLabel } from '@app/enums/source-recording.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';

const RECORDING_ACCEPT = '.mp3,.m4a,.wav';
const RECORDING_MAX_SIZE_MB = 100;
// Source exclut Partage dans cette UI (pas d'écran de partage inter-chorales dans ce lot,
// décision documentée bloc de transfert Q4/Q5).
const ALLOWED_SOURCES: RecordingSourceEnum[] = [RecordingSourceEnum.UploadedFile, RecordingSourceEnum.RecordedInApp];

// Formulaire de dépôt d'un enregistrement (brouillon) — Statut forcé côté back. La durée
// (DurationSeconds) est mesurée côté client via un élément HTML5 <audio> avant l'envoi,
// aucun recalcul serveur (décision documentée, bloc de transfert Q5).
@Component({
  selector: 'app-recording-upload-form',
  standalone: true,
  imports: [ReactiveFormsModule, FileUploadComponent, SongSelectComponent],
  templateUrl: './recording-upload-form.component.html',
  styleUrl: './recording-upload-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RecordingUploadFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly recordingService = inject(RecordingService);
  private readonly destroyRef = inject(DestroyRef);

  readonly songId = input<string | null>(null);

  readonly created = output<IRecording>();
  readonly cancelled = output();

  protected readonly accept = RECORDING_ACCEPT;
  protected readonly maxSizeMo = RECORDING_MAX_SIZE_MB;
  protected readonly getTypeRecordingLabel = getTypeRecordingLabel;
  protected readonly getSourceRecordingLabel = getSourceRecordingLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allTypes: RecordingTypeEnum[] = [RecordingTypeEnum.General, RecordingTypeEnum.ByVoicePart];
  protected readonly allowedSources = ALLOWED_SOURCES;
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];

  readonly selectedType = signal<RecordingTypeEnum>(RecordingTypeEnum.General);
  readonly selectedFile = signal<File | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly durationSecondes = signal<number | null>(null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    songId: this.fb.nonNullable.control('', [Validators.required]),
    type: this.fb.nonNullable.control<RecordingTypeEnum>(RecordingTypeEnum.General, [Validators.required]),
    targetVoicePart: this.fb.control<VoicePartEnum | null>(null),
    ownerContent: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    downloadAllowed: this.fb.nonNullable.control(true),
    source: this.fb.nonNullable.control<RecordingSourceEnum>(RecordingSourceEnum.UploadedFile, [Validators.required])
  });

  // `songId` est un input : il n'est pas disponible a la construction du composant. Le lire
  // dans le constructeur laissait la preselection silencieusement sans effet. L'effect
  // s'execute apres le premier calcul des entrees.
  constructor() {
    effect(() => {
      const preselected = this.songId();
      if (preselected) {
        this.form.controls.songId.setValue(preselected);
      }
    });
  }

  onTypeChange(value: string): void {
    const type = Number(value) as RecordingTypeEnum;
    this.selectedType.set(type);
    this.form.controls.type.setValue(type);
    this.applyVoicePartRequirement(type);
  }

  // Un enregistrement « Par voix » sans voix cible est refusé en 400 par RecordingController.
  // La contrainte est portée ici aussi : sinon le refus n'arrive qu'après le téléversement de
  // l'audio, qui peut peser jusqu'à 100 Mo.
  private applyVoicePartRequirement(type: RecordingTypeEnum): void {
    const control = this.form.controls.targetVoicePart;
    if (type === RecordingTypeEnum.ByVoicePart) {
      control.addValidators(Validators.required);
    } else {
      control.removeValidators(Validators.required);
      control.setValue(null);
    }
    control.updateValueAndValidity();
  }

  onFileSelected(file: File): void {
    this.selectedFile.set(file);
    this.durationSecondes.set(null);
    this.measureDuration(file);
  }

  onFileValidationError(message: string | null): void {
    this.fileError.set(message);
    if (message) {
      this.selectedFile.set(null);
      this.durationSecondes.set(null);
    }
  }

  // Le fichier et sa durée mesurée vivent ici, pas dans FileUploadComponent : sans ce retour,
  // un fichier « retiré » à l'écran restait celui qui partait au dépôt.
  onFileCleared(): void {
    this.selectedFile.set(null);
    this.durationSecondes.set(null);
    this.fileError.set(null);
    this.error.set(null);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    if (this.fileError()) {
      return;
    }
    const file = this.selectedFile();
    if (!file) {
      this.error.set('Merci de sélectionner un fichier audio.');
      return;
    }
    const duration = this.durationSecondes();
    if (duration === null) {
      this.error.set("La durée du fichier n'a pas pu être mesurée. Merci de resélectionner le fichier.");
      return;
    }

    const raw = this.form.getRawValue();
    const request: ICreateRecordingRequest = {
      SongId: raw.songId,
      Type: raw.type,
      TargetVoicePart: raw.targetVoicePart,
      ContentOwner: raw.ownerContent,
      DownloadAllowed: raw.downloadAllowed,
      DurationSeconds: duration,
      Source: raw.source
    };

    this.error.set(null);
    this.isSubmitting.set(true);

    this.recordingService
      .create(file, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: recording => {
          this.isSubmitting.set(false);
          this.created.emit(recording);
        },
        error: () => {
          this.isSubmitting.set(false);
          this.error.set('Impossible de déposer cet enregistrement. Merci de réessayer.');
        }
      });
  }

  cancel(): void {
    this.cancelled.emit();
  }

  private measureDuration(file: File): void {
    const url = URL.createObjectURL(file);
    const audio = new Audio();
    audio.preload = 'metadata';
    audio.addEventListener('loadedmetadata', () => {
      this.durationSecondes.set(Math.round(audio.duration));
      URL.revokeObjectURL(url);
    });
    audio.addEventListener('error', () => {
      URL.revokeObjectURL(url);
      this.fileError.set('Impossible de lire la durée de ce fichier audio.');
      this.selectedFile.set(null);
    });
    audio.src = url;
  }

}
