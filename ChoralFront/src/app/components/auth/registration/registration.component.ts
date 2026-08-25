import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { RegistrationService } from '@app/services/onboarding/registration.service';
import { RoutePaths } from '@core/route-paths';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Reflète la politique de mot de passe ASP.NET Identity côté backend (même contrainte que
// reset-password.component) : majuscule, minuscule, chiffre et caractère spécial requis.
// RegisterViewModel ne porte qu'un [MinLength(8)] côté annotations DTO — la complexité réelle
// est appliquée par Identity lui-même (400 { FrontMessage, ErrorMessages[] } si non respectée).
const PASSWORD_COMPLEXITY_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).+$/;

interface IRegisterErrorBody {
  FrontMessage?: string;
  ErrorMessages?: string[];
}

// Un seul écran d'action : Prénom/Nom/Email/Mot de passe, un unique bouton principal. Aucune
// validation inline "email déjà utilisé" (ce serait un oracle — anti-énumération, cf. `11` §3.1).
// L'email n'est jamais mis en query params vers /registration/confirm (OWASP — pas de PII en
// URL) : transmis via Router navigation state, avec repli explicite si celui-ci est absent
// (rechargement de page) — voir InscriptionConfirmezComponent.
@Component({
  selector: 'app-registration',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './registration.component.html',
  styleUrl: './registration.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationComponent {
  private readonly fb = inject(FormBuilder);
  private readonly registrationService = inject(RegistrationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly form = this.fb.nonNullable.group({
    firstname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    lastname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    password: this.fb.nonNullable.control('', [
      Validators.required,
      Validators.minLength(8),
      Validators.pattern(PASSWORD_COMPLEXITY_PATTERN)
    ])
  });

  togglePasswordVisibility(): void {
    this.showPassword.update(v => !v);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.isSubmitting.set(true);

    const { firstname, lastname, email, password } = this.form.getRawValue();

    this.registrationService
      .register({ Firstname: firstname, Lastname: lastname, Email: email, Password: password })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.router.navigate([`/${RoutePaths.Registration}`, RoutePaths.RegistrationConfirm], { state: { email } });
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          this.error.set(this.extractRegisterErrorMessage(err));
        }
      });
  }

  // Register renvoie un corps d'erreur { FrontMessage, ErrorMessages[] } sur 400 (mot de passe
  // non conforme) — une forme différente de { Message, Errors } que sait lire
  // ApiErrorInterceptor. Le toast global affichera donc un message générique ; ce composant
  // reste responsable d'afficher le détail utile inline.
  private extractRegisterErrorMessage(err: HttpErrorResponse): string {
    const body = err.error as IRegisterErrorBody | undefined;
    if (body?.FrontMessage) return body.FrontMessage;
    if (body?.ErrorMessages && body.ErrorMessages.length > 0) return body.ErrorMessages.slice(0, 3).join(' • ');
    return 'Impossible de créer le compte pour le moment. Merci de réessayer.';
  }
}
