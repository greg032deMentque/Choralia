import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { OnboardingService } from '@app/services/onboarding/onboarding.service';
import { RegistrationService } from '@app/services/onboarding/registration.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { IChoirCreationResult } from '@models/onboarding-models/choir-creation-result.model';
import { SpaceBootstrapComponent } from '@app/components/onboarding/space-bootstrap/space-bootstrap.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';

// Formulaire de création de chorale (POST /api/onboarding/CreateChoir). Structure est
// facultatif : un champ vide ne doit JAMAIS être transmis comme chaîne vide (le back créerait
// un Client nommé "" en silence) — voir buildStructure(). Sur 403 (email non vérifié), écran
// de blocage explicite avec renvoi de lien : jamais un simple bouton grisé sans explication.
@Component({
  selector: 'app-create-choir',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, SpaceBootstrapComponent, IconComponent],
  templateUrl: './create-choir.component.html',
  styleUrl: './create-choir.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class CreateChoirComponent {
  private readonly fb = inject(FormBuilder);
  private readonly onboardingService = inject(OnboardingService);
  private readonly registrationService = inject(RegistrationService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly SpaceTypeEnum = SpaceTypeEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly emailNonVerifie = signal(false);
  readonly resendSent = signal(false);
  readonly created = signal<IChoirCreationResult | null>(null);

  readonly form = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(200)]),
    description: this.fb.nonNullable.control('', [Validators.maxLength(1000)]),
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
      .createChoir({
        Name: raw.name,
        Description: raw.description.trim() || undefined,
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
          this.error.set("Impossible de créer la chorale pour le moment. Merci de réessayer.");
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
