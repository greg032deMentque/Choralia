import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { JoinCodeService } from '@app/services/onboarding/join-code.service';
import { ConfirmService } from '@app/services/confirm.service';
import { RoutePaths } from '@core/route-paths';
import { IJoinCode } from '@models/onboarding-models/join-code.model';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

const DEFAULT_DURATION_DAYS = 30;
const MAX_DURATION_DAYS = 90;
const COPIED_FEEDBACK_MS = 2000;

// Panneau réutilisable de management du code de rattachement d'un espace (chorale ou événement),
// affiché à la fois dans l'écran d'amorçage post-création (space-bootstrap.component) et dans
// le segment Demandes de /management/:spaceId/membres — un seul endroit pour consulter/faire
// tourner/désactiver le code (décision produit : "dans les paramètres de l'espace ou l'écran
// d'amorçage"). La rotation et la désactivation tuent le code courant : ConfirmService est
// obligatoire avant ces deux actions ; une toute première génération (aucun code actif) ne
// détruit rien, pas de confirmation nécessaire.
@Component({
  selector: 'app-join-code-panel',
  standalone: true,
  imports: [ReactiveFormsModule, IconComponent, DatePipe],
  templateUrl: './join-code-panel.component.html',
  styleUrl: './join-code-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class JoinCodePanelComponent {
  private readonly joinCodeService = inject(JoinCodeService);
  private readonly confirmService = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly spaceId = input.required<string>();

  protected readonly IconNameEnum = IconNameEnum;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly joinCode = signal<IJoinCode | null>(null);
  readonly isBusy = signal(false);
  readonly copiedFeedback = signal<'code' | 'lien' | null>(null);

  readonly durationForm = this.fb.nonNullable.group({
    durationDays: this.fb.nonNullable.control(DEFAULT_DURATION_DAYS, [Validators.required, Validators.min(1), Validators.max(MAX_DURATION_DAYS)])
  });

  readonly joinLink = computed(() => {
    const code = this.joinCode()?.Code;
    if (!code) return null;
    return `${window.location.origin}/${RoutePaths.Join}?code=${encodeURIComponent(code)}`;
  });

  // Le chargement passe par un effect() et non par le constructeur : `spaceId` est un
  // `input.required`, jamais disponible au moment de la construction d'un composant instancié
  // par binding de template — le lire là lève NG0950. L'effect s'exécute après le premier
  // calcul des entrées, et se relance si le composant est réutilisé avec un autre espace.
  constructor() {
    effect(() => {
      const spaceId = this.spaceId();
      if (!spaceId) return;
      this.load(spaceId);
    });
  }

  async generateOuRotator(): Promise<void> {
    if (this.durationForm.invalid) {
      this.durationForm.markAllAsTouched();
      return;
    }

    const dejaActive = this.joinCode()?.IsActive === true;
    if (dejaActive) {
      const confirmed = await this.confirmService.confirm({
        title: 'Faire tourner le code de rattachement ?',
        message: "L'ancien code cessera de fonctionner immédiatement. Toute personne qui ne l'a pas encore utilisé devra recevoir le nouveau.",
        confirmationLabel: 'Faire tourner le code',
        danger: true
      });
      if (!confirmed) return;
    }

    const { durationDays } = this.durationForm.getRawValue();
    this.isBusy.set(true);
    this.error.set(null);

    this.joinCodeService
      .generateOuRotator(this.spaceId(), durationDays)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.isBusy.set(false);
          this.joinCode.set(result);
        },
        error: () => {
          this.isBusy.set(false);
          this.error.set('Impossible de générer le code pour le moment. Merci de réessayer.');
        }
      });
  }

  async desactiver(): Promise<void> {
    const confirmed = await this.confirmService.confirm({
      title: 'Désactiver le code de rattachement ?',
      message: 'Plus personne ne pourra vous rejoindre avec ce code une fois désactivé.',
      confirmationLabel: 'Désactiver',
      danger: true
    });
    if (!confirmed) return;

    this.isBusy.set(true);
    this.error.set(null);

    this.joinCodeService
      .desactiver(this.spaceId())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.isBusy.set(false);
          this.joinCode.set({ Code: null, ExpiresAt: null, IsActive: false });
        },
        error: () => {
          this.isBusy.set(false);
          this.error.set('Impossible de désactiver le code pour le moment. Merci de réessayer.');
        }
      });
  }

  copierCode(): void {
    const code = this.joinCode()?.Code;
    if (!code) return;
    void navigator.clipboard.writeText(code);
    this.showCopiedFeedback('code');
  }

  copierLien(): void {
    const lien = this.joinLink();
    if (!lien) return;
    void navigator.clipboard.writeText(lien);
    this.showCopiedFeedback('lien');
  }

  private showCopiedFeedback(kind: 'code' | 'lien'): void {
    this.copiedFeedback.set(kind);
    setTimeout(() => this.copiedFeedback.set(null), COPIED_FEEDBACK_MS);
  }

  private load(spaceId: string): void {
    this.loading.set(true);
    this.error.set(null);

    this.joinCodeService
      .getActive(spaceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.loading.set(false);
          this.joinCode.set(result);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger le code de rattachement.');
        }
      });
  }
}
