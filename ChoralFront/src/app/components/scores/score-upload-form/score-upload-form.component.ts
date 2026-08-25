import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ScoreService } from '@app/services/scores/score.service';
import { FileUploadComponent } from '@app/components/shared/file-upload/file-upload.component';
import { SongSelectComponent } from '@app/components/shared/song-select/song-select.component';
import { IScore } from '@models/scores-models/score.model';
import { ICreateScoreRequest } from '@models/scores-models/create-score-request.model';
import { ScoreTypeEnum, getTypeScoreLabel } from '@app/enums/type-score.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';

const SCORE_ACCEPT = '.pdf,.png,.jpg,.jpeg';
const SCORE_MAX_SIZE_MB = 20;

// Formulaire de dépôt d'une partition (brouillon) — Statut forcé côté back, jamais envoyé
// ici. Whitelist de formats et taille max imposées par le bloc de transfert (Q3 métier).
@Component({
  selector: 'app-score-upload-form',
  standalone: true,
  imports: [ReactiveFormsModule, FileUploadComponent, SongSelectComponent],
  templateUrl: './score-upload-form.component.html',
  styleUrl: './score-upload-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScoreUploadFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly scoreService = inject(ScoreService);
  private readonly destroyRef = inject(DestroyRef);

  readonly songId = input<string | null>(null);

  readonly created = output<IScore>();
  readonly cancelled = output();

  protected readonly accept = SCORE_ACCEPT;
  protected readonly maxSizeMo = SCORE_MAX_SIZE_MB;
  protected readonly getTypeScoreLabel = getTypeScoreLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allTypes: ScoreTypeEnum[] = [ScoreTypeEnum.General, ScoreTypeEnum.ByVoicePart];
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];

  readonly selectedType = signal<ScoreTypeEnum>(ScoreTypeEnum.General);
  readonly selectedFile = signal<File | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    songId: this.fb.nonNullable.control('', [Validators.required]),
    type: this.fb.nonNullable.control<ScoreTypeEnum>(ScoreTypeEnum.General, [Validators.required]),
    targetVoicePart: this.fb.control<VoicePartEnum | null>(null),
    version: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(50)]),
    downloadAllowed: this.fb.nonNullable.control(true)
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
    const type = Number(value) as ScoreTypeEnum;
    this.selectedType.set(type);
    this.form.controls.type.setValue(type);
    this.applyVoicePartRequirement(type);
  }

  onFileSelected(file: File): void {
    this.selectedFile.set(file);
  }

  onFileValidationError(message: string | null): void {
    this.fileError.set(message);
    if (message) {
      this.selectedFile.set(null);
    }
  }

  // Le fichier vit ici, pas dans FileUploadComponent : sans ce retour, un fichier « retiré »
  // à l'écran restait celui qui partait au dépôt.
  onFileCleared(): void {
    this.selectedFile.set(null);
    this.fileError.set(null);
    this.error.set(null);
  }

  // Une partition « Par voix » sans voix cible est refusée en 400 par ScoreController. La
  // contrainte est portée ici aussi : sinon le refus n'arrive qu'après l'envoi du fichier.
  private applyVoicePartRequirement(type: ScoreTypeEnum): void {
    const control = this.form.controls.targetVoicePart;
    if (type === ScoreTypeEnum.ByVoicePart) {
      control.addValidators(Validators.required);
    } else {
      control.removeValidators(Validators.required);
      control.setValue(null);
    }
    control.updateValueAndValidity();
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
      this.error.set('Merci de sélectionner un fichier.');
      return;
    }

    const raw = this.form.getRawValue();
    const request: ICreateScoreRequest = {
      SongId: raw.songId,
      Type: raw.type,
      TargetVoicePart: raw.targetVoicePart,
      Version: raw.version,
      DownloadAllowed: raw.downloadAllowed
    };

    this.error.set(null);
    this.isSubmitting.set(true);

    this.scoreService
      .create(file, request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: score => {
          this.isSubmitting.set(false);
          this.created.emit(score);
        },
        error: () => {
          this.isSubmitting.set(false);
          this.error.set('Impossible de déposer cette partition. Merci de réessayer.');
        }
      });
  }

  cancel(): void {
    this.cancelled.emit();
  }

}
