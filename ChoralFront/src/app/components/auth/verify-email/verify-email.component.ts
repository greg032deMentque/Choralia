import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RegistrationService } from '@app/services/onboarding/registration.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

const REDIRECT_DELAY_MS = 1500;

// Query params liés via withComponentInputBinding() (app.config.ts) : jamais de
// paramMap.get() nu. userId (identifiant ASP.NET Identity, format GUID) est validé par
// isValidUuid avant tout appel HTTP (OWASP A01) ; token est une chaîne opaque (pas un UUID),
// seule sa présence est vérifiée — fallback explicite si absent.
@Component({
  selector: 'app-verify-email',
  standalone: true,
  imports: [RouterLink, IconComponent],
  templateUrl: './verify-email.component.html',
  styleUrl: './verify-email.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class VerifyEmailComponent {
  private readonly registrationService = inject(RegistrationService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly userId = input<string | undefined>(undefined);
  readonly token = input<string | undefined>(undefined);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly loading = signal(true);
  readonly verified = signal(false);
  readonly error = signal<string | null>(null);

  private verificationLancee = false;

  // `userId` et `token` sont des inputs liés aux query params par withComponentInputBinding() :
  // ils ne sont PAS disponibles à la construction du composant. Les lire dans le constructeur
  // donnait toujours `undefined`, donc l'écran affichait « Lien de vérification invalide ou
  // expiré. » sans jamais appeler l'API — la vérification d'email était inopérante pour tout
  // le monde, alors que le jeton était valide (vérifié : appel direct de l'endpoint -> 204).
  // L'effect s'exécute après le premier calcul des entrées ; le drapeau évite qu'une
  // réémission des inputs relance un second appel.
  constructor() {
    effect(() => {
      const userId = this.userId();
      const token = this.token();
      if (this.verificationLancee) return;
      this.verificationLancee = true;
      this.verify(userId, token);
    });
  }

  private verify(userId: string | undefined, token: string | undefined): void {
    if (!isValidUuid(userId) || !token) {
      this.loading.set(false);
      this.error.set('Lien de vérification invalide ou expiré.');
      return;
    }

    this.registrationService
      .verifyEmail(userId, token)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.loading.set(false);
          this.verified.set(true);
          setTimeout(() => {
            const target = this.authStore.isAuthenticated() ? this.authStore.currentZone().path : `/${RoutePaths.Login}`;
            this.router.navigateByUrl(target);
          }, REDIRECT_DELAY_MS);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Lien de vérification invalide ou expiré.');
        }
      });
  }
}
