import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@app/services/auth/auth.service';
import { RoutePaths } from '@core/route-paths';
import { PASSWORD_COMPLEXITY_PATTERN, PASSWORD_MIN_LENGTH, passwordsMatchValidator } from '@core/password.validators';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Écran d'activation du compte d'un membre invité : dernière étape du parcours d'invitation
// (le responsable invite → le membre reçoit un mail → ce lien). userId/token sont liés en
// query params via withComponentInputBinding() (app.config.ts) — jamais de paramMap.get() nu,
// et ils sont vérifiés présents avant tout appel HTTP (OWASP A01).
//
// Le back confond volontairement toutes les causes d'échec (jeton illisible, expiré, déjà
// consommé, compte inconnu) derrière un unique message : ne jamais introduire ici de message
// distinguant ces cas, ce serait rouvrir l'oracle d'existence de compte que le back ferme.
@Component({
  selector: 'app-activate-account',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './activate-account.component.html',
  styleUrl: './activate-account.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ActivateAccountComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly userId = input<string | undefined>(undefined);
  readonly token = input<string | undefined>(undefined);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly submitted = signal(false);

  readonly hasValidParams = computed(() => Boolean(this.userId()) && Boolean(this.token()));

  // Noms de contrôles imposés par passwordsMatchValidator (core/password.validators.ts).
  readonly form = this.fb.nonNullable.group(
    {
      newPassword: this.fb.nonNullable.control('', [
        Validators.required,
        Validators.minLength(PASSWORD_MIN_LENGTH),
        Validators.pattern(PASSWORD_COMPLEXITY_PATTERN)
      ]),
      confirmPassword: this.fb.nonNullable.control('', [Validators.required])
    },
    { validators: passwordsMatchValidator }
  );

  submit(): void {
    // Lecture locale plutôt que hasValidParams() + assertion non-nulle : le narrowing
    // TypeScript porte alors sur les valeurs réellement envoyées, sans `!` (règle ESLint
    // @typescript-eslint/no-non-null-assertion).
    const userId = this.userId();
    const token = this.token();
    if (!userId || !token) {
      this.error.set('Ce lien d\'activation est invalide ou incomplet.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.isSubmitting.set(true);

    this.authService.activateAccount({
      UserId: userId,
      Token: token,
      NewPassword: this.form.getRawValue().newPassword
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set(
          'Ce lien d\'activation est invalide ou expiré. Demandez une nouvelle invitation au responsable de votre chorale.'
        );
      }
    });
  }
}
