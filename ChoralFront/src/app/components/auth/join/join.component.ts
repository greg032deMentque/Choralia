import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { OnboardingService } from '@app/services/onboarding/onboarding.service';
import { AuthService } from '@app/services/auth/auth.service';
import { AuthStore } from '@core/auth.store';
import { StorageService } from '@app/services/storage.service';
import { RoutePaths } from '@core/route-paths';
import { IPreviewCode } from '@models/onboarding-models/preview-code.model';
import { getSpaceTypeLabel } from '@app/enums/space-type.enum';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Accessible CONNECTÉ ET NON CONNECTÉ — ni guestGuard (bloquerait un utilisateur déjà
// connecté qui reçoit un lien d'invitation) ni authGuard (bloquerait un visiteur anonyme qui
// veut juste voir le nom de l'espace avant de créer un compte) ne conviennent. Mode de garde
// retenu : AUCUN guard — route publique (app.routes.ts), branchement interne sur
// AuthStore.isAuthenticated() pour proposer la bonne action. C'est le "troisième mode" demandé.
//
// Query param `code` lié via withComponentInputBinding() : quand présent, PreviewCode
// (AllowAnonymous) est appelé immédiatement et seul le nom + le type de l'espace sont
// affichés avant toute saisie — rien d'autre n'est renvoyé par le back, rien d'autre n'est
// affiché ici (décision produit : pas de nombre de membres).
@Component({
  selector: 'app-join',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './join.component.html',
  styleUrl: './join.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JoinComponent {
  private readonly onboardingService = inject(OnboardingService);
  private readonly authService = inject(AuthService);
  private readonly authStore = inject(AuthStore);
  private readonly storage = inject(StorageService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly code = input<string | undefined>(undefined);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getSpaceTypeLabel = getSpaceTypeLabel;

  readonly isAuthenticated = this.authStore.isAuthenticated;
  readonly currentUser = this.authStore.user;

  readonly loadingPreview = signal(false);
  readonly previewError = signal<string | null>(null);
  readonly preview = signal<IPreviewCode | null>(null);

  readonly isSubmittingRequest = signal(false);
  readonly requestError = signal<string | null>(null);
  readonly requestEnvoyee = signal(false);

  readonly manualCodeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required])
  });

  readonly requestForm = this.fb.nonNullable.group({
    message: this.fb.nonNullable.control('', [Validators.maxLength(500)])
  });

  constructor() {
    // Réagit à toute évolution du query param `code` (navigation vers la même route avec un
    // code différent, ex. depuis le formulaire manuel ci-dessous).
    effect(() => {
      const code = this.code();
      if (!code) {
        this.preview.set(null);
        this.previewError.set(null);
        return;
      }
      this.loadPreview(code);
    });
  }

  private loadPreview(code: string): void {
    this.loadingPreview.set(true);
    this.previewError.set(null);
    this.preview.set(null);
    this.requestEnvoyee.set(false);
    this.requestError.set(null);

    this.onboardingService
      .previewCode(code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.loadingPreview.set(false);
          this.preview.set(result);
        },
        error: (err: HttpErrorResponse) => {
          this.loadingPreview.set(false);
          this.previewError.set(this.extractPreviewErrorMessage(err));
        }
      });
  }

  // Message unique du serveur remonté tel quel (anti-énumération) — jamais de déduction de
  // cause côté front. Seul le 429 a un message dédié distinct du 400 générique.
  private extractPreviewErrorMessage(err: HttpErrorResponse): string {
    if (err.status === 429) {
      return (err.error as { Message?: string } | undefined)?.Message ?? 'Trop de tentatives. Merci de réessayer plus tard.';
    }
    return (err.error as { Message?: string } | undefined)?.Message ?? 'Code inconnu ou expiré.';
  }

  submitManualCode(): void {
    if (this.manualCodeForm.invalid) {
      this.manualCodeForm.markAllAsTouched();
      return;
    }

    const { code } = this.manualCodeForm.getRawValue();
    this.router.navigate([`/${RoutePaths.Join}`], { queryParams: { code } });
  }

  loginWithReturnUrl(): void {
    const returnUrl = `/${RoutePaths.Join}?code=${encodeURIComponent(this.code() ?? '')}`;
    this.router.navigate([`/${RoutePaths.Login}`], { queryParams: { returnUrl } });
  }

  // "Utiliser un autre compte" : termine la session courante avant de renvoyer vers /login
  // avec le même code en returnUrl, pour ne pas perdre le fil de la demande de rattachement.
  useAnotherAccount(): void {
    const returnUrl = `/${RoutePaths.Join}?code=${encodeURIComponent(this.code() ?? '')}`;
    this.authService
      .logout({ RefreshToken: this.storage.GetRefreshToken() ?? undefined, DeviceId: this.storage.GetDeviceId() ?? undefined })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.router.navigate([`/${RoutePaths.Login}`], { queryParams: { returnUrl } }),
        error: () => {
          this.authStore.clear();
          this.router.navigate([`/${RoutePaths.Login}`], { queryParams: { returnUrl } });
        }
      });
  }

  requestMembership(): void {
    const code = this.code();
    if (!code || this.requestForm.invalid) return;

    this.isSubmittingRequest.set(true);
    this.requestError.set(null);

    const { message } = this.requestForm.getRawValue();

    this.onboardingService
      .requestMembership({ Code: code, Message: message || undefined })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isSubmittingRequest.set(false);
          this.requestEnvoyee.set(true);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmittingRequest.set(false);
          this.requestError.set((err.error as { Message?: string } | undefined)?.Message ?? 'Impossible d\'enregistrer votre demande pour le moment.');
        }
      });
  }
}
