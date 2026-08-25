import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongListService } from '@app/services/song-lists/song-list.service';
import { EventService } from '@app/services/events/event.service';
import { AuthStore } from '@core/auth.store';
import { ISongList } from '@models/song-lists-models/song-list.model';
import { ISelectOption } from '@models/common-models/select-option.model';
import { SongListTypeEnum, getTypeListLabel } from '@app/enums/type-list.enum';
import { SongListStatusEnum } from '@app/enums/status-list.enum';

const ALL_TYPES: SongListTypeEnum[] = [SongListTypeEnum.Free, SongListTypeEnum.Event, SongListTypeEnum.Season, SongListTypeEnum.Section];
// Plafonné à 100 : PaginateViewModel.PageSize porte [Range(1, 100)] côté back — au-delà,
// l'appel repart en 400 et le sélecteur reste vide.
const EVENT_OPTIONS_PAGE_SIZE = 100;

// Formulaire création/édition — mode déterminé par la présence de l'input `songList`
// (édition si non-null). Statut et OwnerUserId ne sont jamais renseignés depuis ce
// formulaire : le mapping ViewModel -> Entity côté back les ignore explicitement sur
// Create/Update (Statut géré par les endpoints Publish/Archive/RevertToDraft,
// OwnerUserId par le service métier back). SectionId hors périmètre de ce lot
// (aucun SectionService/liste de pupitres disponible côté front à ce stade).
@Component({
  selector: 'app-song-list-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './song-list-form.component.html',
  styleUrl: './song-list-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongListFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly songListService = inject(SongListService);
  private readonly eventService = inject(EventService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly songList = input<ISongList | null>(null);

  readonly saved = output<ISongList>();
  readonly cancelled = output();

  protected readonly getTypeListLabel = getTypeListLabel;
  protected readonly allTypes = ALL_TYPES;

  readonly isEditMode = computed(() => this.songList() !== null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly eventOptions = signal<ISelectOption<string>[]>([]);

  readonly form = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    description: this.fb.control<string | null>(null, [Validators.maxLength(500)]),
    type: this.fb.nonNullable.control<SongListTypeEnum>(SongListTypeEnum.Free, [Validators.required]),
    eventId: this.fb.control<string | null>(null)
  });

  // FormControl.valeur n'est pas un signal — un computed() qui le lirait directement ne se
  // réévaluerait jamais au changement. selectedType est synchronisé explicitement via
  // valueChanges pour piloter l'affichage conditionnel du sélecteur d'événement.
  private readonly selectedType = signal<SongListTypeEnum>(SongListTypeEnum.Free);
  readonly showEventSelect = computed(() => this.selectedType() === SongListTypeEnum.Event);

  constructor() {
    this.loadEventOptions();

    effect(() => {
      const current = this.songList();
      if (current) {
        this.form.patchValue({
          name: current.Name,
          description: current.Description,
          type: current.Type,
          eventId: current.EventId
        });
        this.selectedType.set(current.Type);
      } else {
        this.form.reset({ name: '', description: null, type: SongListTypeEnum.Free, eventId: null });
        this.selectedType.set(SongListTypeEnum.Free);
      }
    });

    this.form.controls.type.valueChanges.pipe(takeUntilDestroyed()).subscribe(type => this.selectedType.set(type));
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
    const existing = this.songList();

    const payload: ISongList = {
      Id: existing?.Id ?? null,
      Name: raw.name,
      Description: raw.description,
      ChoirId: activeChoirId,
      SectionId: existing?.SectionId ?? null,
      EventId: raw.type === SongListTypeEnum.Event ? raw.eventId : null,
      CreatedById: existing?.CreatedById ?? null,
      OwnerUserId: existing?.OwnerUserId ?? null,
      Type: raw.type,
      Status: existing?.Status ?? SongListStatusEnum.Draft,
      Songs: existing?.Songs ?? []
    };

    this.error.set(null);
    this.isSubmitting.set(true);

    const request$ = existing?.Id ? this.songListService.update(existing.Id, payload) : this.songListService.create(payload);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.isSubmitting.set(false);
        this.saved.emit(result);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set("Impossible d'enregistrer la liste de chants. Merci de réessayer.");
      }
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }

  private loadEventOptions(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) return;

    this.eventService
      .getPaged(choirId, { Page: 1, PageSize: EVENT_OPTIONS_PAGE_SIZE, SortActive: 'Title', SortDirection: 'asc' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.eventOptions.set(result.Items.map(e => ({ Value: e.Id ?? '', Label: e.Title })));
        },
        error: () => this.error.set('Impossible de charger la liste des événements.')
      });
  }
}
