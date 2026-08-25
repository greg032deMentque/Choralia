import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { map, Observable, tap } from 'rxjs';
import { RegistrationService } from '@app/services/onboarding/registration.service';
import { RoutePaths } from '@core/route-paths';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';

interface INavigationEmailState {
  email?: string;
}

// Écran de sortie SANS état serveur : Register répond toujours 200 avec le même message
// (anti-énumération), donc rien à recharger ici. L'email est reçu par Router navigation state
// (jamais en query params — pas de PII en URL) ; s'il est absent (rechargement de page, accès
// direct), un petit formulaire de repli permet de le ressaisir pour renvoyer le message —
// jamais de cul-de-sac (décision produit : tout écran d'attente/sortie garde une action).
@Component({
  selector: 'app-registration-confirm',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent, SubmitOnceDirective],
  templateUrl: './registration-confirm.component.html',
  styleUrl: './registration-confirm.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RegistrationConfirmComponent {
  private readonly fb = inject(FormBuilder);
  private readonly registrationService = inject(RegistrationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly email = signal<string | null>(null);
  readonly resent = signal(false);

  readonly fallbackForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email])
  });

  constructor() {
    const navigationState = this.router.getCurrentNavigation()?.extras.state as INavigationEmailState | undefined;
    const persistedState = history.state as INavigationEmailState | undefined;
    this.email.set(navigationState?.email ?? persistedState?.email ?? null);
  }

  // Action pour SubmitOnceDirective quand l'email est connu (state de navigation présent).
  resendKnownEmail = (): Observable<void> => {
    const email = this.email();
    return this.registrationService.resendVerification({ Email: email ?? '' })
      .pipe(map(() => undefined), tap(() => this.resent.set(true)));
  };

  submitFallback(): void {
    if (this.fallbackForm.invalid) {
      this.fallbackForm.markAllAsTouched();
      return;
    }

    const { email } = this.fallbackForm.getRawValue();
    this.registrationService
      .resendVerification({ Email: email })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.resent.set(true));
  }
}
