import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { OnboardingService } from '@app/services/onboarding/onboarding.service';
import { RegistrationService } from '@app/services/onboarding/registration.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { IEvent } from '@models/events-models/event.model';
import { EventTypeEnum, getEventTypeLabel } from '@app/enums/event-type.enum';
import { SpaceBootstrapComponent } from '@app/components/onboarding/space-bootstrap/space-bootstrap.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';

const ALL_TYPES: EventTypeEnum[] = [
  EventTypeEnum.Concert,
  EventTypeEnum.Rehearsal,
  EventTypeEnum.Wedding,
  EventTypeEnum.Mass,
  EventTypeEnum.Funeral,
  EventTypeEnum.Other
];

// Formulaire de création d'un événement autonome (POST /api/onboarding/CreateEvent) — même
// logique que CreateChoirComponent pour Structure (facultatif, jamais transmis en chaîne
// vide) et le blocage 403 email non vérifié.
@Component({
  selector: 'app-create-event',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, SpaceBootstrapComponent, IconComponent],
  templateUrl: './create-event.component.html',
  styleUrl: './create-event.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreateEventComponent {
  private readonly fb = inject(FormBuilder);
  private readonly onboardingService = inject(OnboardingService);
  private readonly registrationService = inject(RegistrationService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly SpaceTypeEnum = SpaceTypeEnum;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly allTypes = ALL_TYPES;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly emailNonVerifie = signal(false);
  readonly resendSent = signal(false);
  readonly created = signal<IEvent | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.fb.nonNullable.control('', [Validators.maxLength(1000)]),
    dateDebut: this.fb.nonNullable.control('', [Validators.required]),
    dateFin: this.fb.nonNullable.control(''),
    type: this.fb.nonNullable.control<EventTypeEnum>(EventTypeEnum.Concert, [Validators.required]),
    location: this.fb.nonNullable.control('', [Validators.maxLength(300)]),
    structure: this.fb.nonNullable.control('', [Validators.maxLength(150)])
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.emailNonVerifie.set(false);
    this.isSubmitting.set(true);

    const raw = this.form.getRawValue();

    this.onboardingService
      .createEvent({
        Title: raw.title,
        Description: raw.description.trim() || undefined,
        StartDate: fromDatetimeLocal(raw.dateDebut) ?? raw.dateDebut,
        EndDate: raw.dateFin ? (fromDatetimeLocal(raw.dateFin) ?? undefined) : undefined,
        Type: raw.type,
        Location: raw.location.trim() || undefined,
        Structure: raw.structure.trim() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.isSubmitting.set(false);
          this.created.set(result);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          if (err.status === 403) {
            this.emailNonVerifie.set(true);
            return;
          }
          this.error.set("Impossible de créer l'événement pour le moment. Merci de réessayer.");
        }
      });
  }

  rsendVerification(): void {
    const email = this.authStore.user()?.Email;
    if (!email) return;

    this.registrationService
      .resendVerification({ Email: email })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.resendSent.set(true));
  }
}

// input[type=datetime-local] rend "yyyy-MM-ddTHH:mm" en heure locale — conversion vers ISO
// attendue par CreateEventViewModel.StartDate/EndDate.
function fromDatetimeLocal(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}
