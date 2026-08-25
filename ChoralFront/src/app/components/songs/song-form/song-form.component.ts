import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { AuthStore } from '@core/auth.store';
import { ISong } from '@models/songs-models/song.model';
import { SongStatusEnum, getSongStatusLabel } from '@app/enums/song-status.enum';
import { SongPriorityEnum, getPrioritySongLabel } from '@app/enums/priority-song.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';

function voicePartMinOneValidator(control: AbstractControl): ValidationErrors | null {
  const value = control.value as VoicePartEnum[];
  return Array.isArray(value) && value.length > 0 ? null : { voicePartRequired: true };
}

const ALL_VOICE_PARTS: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];
const ALL_STATUSES: SongStatusEnum[] = [SongStatusEnum.Active, SongStatusEnum.Archived];
const ALL_PRIORITIES: SongPriorityEnum[] = [SongPriorityEnum.Low, SongPriorityEnum.Normal, SongPriorityEnum.High];

// Formulaire création/édition — mode déterminé par la présence de l'input `chant` (édition
// si non-null). Le parent (SongListComponent/SongDetailComponent) gère la visibilité du
// bouton qui affiche ce formulaire (rôle Responsable/SectionLeader uniquement).
@Component({
  selector: 'app-song-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './song-form.component.html',
  styleUrl: './song-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly songService = inject(SongService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly song = input<ISong | null>(null);

  readonly saved = output<ISong>();
  readonly cancelled = output();

  protected readonly getSongStatusLabel = getSongStatusLabel;
  protected readonly getPrioritySongLabel = getPrioritySongLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allVoicePart = ALL_VOICE_PARTS;
  protected readonly allStatuss = ALL_STATUSES;
  protected readonly allPrioritys = ALL_PRIORITIES;

  readonly isEditMode = computed(() => this.song() !== null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    status: this.fb.nonNullable.control<SongStatusEnum>(SongStatusEnum.Active, [Validators.required]),
    voiceParts: this.fb.nonNullable.control<VoicePartEnum[]>([], [voicePartMinOneValidator]),
    author: this.fb.control<string | null>(null),
    composer: this.fb.control<string | null>(null),
    language: this.fb.control<string | null>(null),
    approximateDurationSeconds: this.fb.control<number | null>(null, [Validators.min(0)]),
    workingKey: this.fb.control<string | null>(null),
    priority: this.fb.control<SongPriorityEnum | null>(null),
    preparationNotes: this.fb.control<string | null>(null)
  });

  constructor() {
    effect(() => {
      const current = this.song();
      if (current) {
        this.form.patchValue({
          title: current.Title,
          status: current.Status,
          voiceParts: [...current.VoiceParts],
          author: current.Author,
          composer: current.Composer,
          language: current.Language,
          approximateDurationSeconds: current.ApproximateDurationSeconds,
          workingKey: current.WorkingKey,
          priority: current.Priority,
          preparationNotes: current.PreparationNotes
        });
      } else {
        this.form.reset({ title: '', status: SongStatusEnum.Active, voiceParts: [] });
      }
    });
  }

  isVoicePartSelected(voicePart: VoicePartEnum): boolean {
    return this.form.controls.voiceParts.value.includes(voicePart);
  }

  toggleVoicePart(voicePart: VoicePartEnum): void {
    const control = this.form.controls.voiceParts;
    const current = control.value;
    const next = current.includes(voicePart) ? current.filter(v => v !== voicePart) : [...current, voicePart];
    control.setValue(next);
    control.markAsTouched();
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const activeChoirId = this.authStore.activeSpaceId();
    if (!activeChoirId) {
      this.error.set('Aucune chorale actif sélectionnée.');
      return;
    }

    const raw = this.form.getRawValue();
    const existing = this.song();

    const payload: ISong = {
      Id: existing?.Id ?? null,
      Title: raw.title,
      Status: raw.status,
      VoiceParts: raw.voiceParts,
      Author: raw.author,
      Composer: raw.composer,
      Language: raw.language,
      ApproximateDurationSeconds: raw.approximateDurationSeconds,
      WorkingKey: raw.workingKey,
      Priority: raw.priority,
      PreparationNotes: raw.preparationNotes,
      ChoirId: activeChoirId,
      IsCompleteForChoir: existing?.IsCompleteForChoir ?? false,
      VoicePartsWithoutPublishedRecording: existing?.VoicePartsWithoutPublishedRecording ?? []
    };

    this.error.set(null);
    this.isSubmitting.set(true);

    const request$ = existing?.Id ? this.songService.update(existing.Id, payload) : this.songService.create(payload);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.isSubmitting.set(false);
        this.saved.emit(result);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set("Impossible d'enregistrer le chant. Merci de réessayer.");
      }
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
