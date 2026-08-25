import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EventService } from '@app/services/events/event.service';
import { AuthStore } from '@core/auth.store';
import { IEvent } from '@models/events-models/event.model';
import { EventTypeEnum, getEventTypeLabel } from '@app/enums/event-type.enum';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventEffectiveStateEnum } from '@app/enums/event-effective-state.enum';

const ALL_TYPES: EventTypeEnum[] = [
  EventTypeEnum.Concert,
  EventTypeEnum.Rehearsal,
  EventTypeEnum.Wedding,
  EventTypeEnum.Mass,
  EventTypeEnum.Funeral,
  EventTypeEnum.Other
];

// Formulaire création/édition — mode déterminé par la présence de l'input `evenement`
// (édition si non-null). Le parent gère la visibilité du bouton qui affiche ce formulaire
// (rôle Responsable uniquement — management des événements non déléguée au SectionLeader).
@Component({
  selector: 'app-event-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './event-form.component.html',
  styleUrl: './event-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly eventService = inject(EventService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly evt = input<IEvent | null>(null);

  readonly saved = output<IEvent>();
  readonly cancelled = output();

  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly allTypes = ALL_TYPES;

  readonly isEditMode = computed(() => this.evt() !== null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.fb.control<string | null>(null, [Validators.maxLength(1000)]),
    dateDebut: this.fb.nonNullable.control('', [Validators.required]),
    dateFin: this.fb.control<string | null>(null),
    type: this.fb.nonNullable.control<EventTypeEnum>(EventTypeEnum.Concert, [Validators.required]),
    // Optionnel à la création — requis uniquement pour Publish (le back renvoie 400 sinon,
    // vérifié côté detail via le bouton Publish désactivé tant que Lieu est vide).
    location: this.fb.nonNullable.control('', [Validators.maxLength(300)])
  });

  constructor() {
    effect(() => {
      const current = this.evt();
      if (current) {
        this.form.patchValue({
          title: current.Title,
          description: current.Description,
          dateDebut: toDatetimeLocal(current.StartDate),
          dateFin: toDatetimeLocal(current.EndDate),
          type: current.Type,
          location: current.Location
        });
      } else {
        this.form.reset({ title: '', description: null, dateDebut: '', dateFin: null, type: EventTypeEnum.Concert, location: '' });
      }
    });
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
    const existing = this.evt();

    const payload: IEvent = {
      Id: existing?.Id ?? null,
      Title: raw.title,
      Description: raw.description,
      StartDate: fromDatetimeLocal(raw.dateDebut) ?? raw.dateDebut,
      EndDate: raw.dateFin ? (fromDatetimeLocal(raw.dateFin) ?? raw.dateFin) : null,
      Type: raw.type,
      Location: raw.location,
      // Statut/EffectiveState/ClosedAt ne sont jamais pilotés par Create/Update (uniquement par
      // EventService.changerStatut) — on renvoie l'état courant en édition, les valeurs
      // par défaut d'un événement neuf en création, pour respecter le contrat IEvent complet.
      Status: existing?.Status ?? EventStatusEnum.Draft,
      EffectiveState: existing?.EffectiveState ?? EventEffectiveStateEnum.Draft,
      // Ce formulaire vit exclusivement dans la zone /management (espace chorale actif) : l'événement
      // est donc toujours rattaché, jamais autonome. ClientId ne s'applique qu'aux événements
      // autonomes (ignoré côté back tant que ChoirId est fourni) — on répercute simplement la
      // valeur existante en édition, null en création.
      ChoirId: activeChoirId,
      ClientId: existing?.ClientId ?? null,
      ClosedAt: existing?.ClosedAt ?? null
    };

    this.error.set(null);
    this.isSubmitting.set(true);

    const request$ = existing?.Id ? this.eventService.update(existing.Id, payload) : this.eventService.create(payload);

    request$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.isSubmitting.set(false);
        this.saved.emit(result);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set("Impossible d'enregistrer l'événement. Merci de réessayer.");
      }
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}

// input[type=datetime-local] attend/rend "yyyy-MM-ddTHH:mm" en heure locale — conversion
// aller-retour avec les chaînes ISO (DateTime) renvoyées/attendues par le back.
function toDatetimeLocal(iso: string | null): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (n: number): string => n.toString().padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function fromDatetimeLocal(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
