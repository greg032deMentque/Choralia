import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, tap } from 'rxjs';
import { OnboardingService } from '@app/services/onboarding/onboarding.service';
import { RoutePaths } from '@core/route-paths';
import { IMyRequest } from '@models/onboarding-models/my-request.model';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';

const MY_REQUESTS_PAGE_SIZE = 50;

// État vide du compte : hub post-connexion pour un utilisateur sans aucun rattachement (target
// de zone-resolver.ts pour 'no-space'), aussi accessible directement (RoutePaths.Start)
// pour rejoindre/créer un espace supplémentaire. "Rejoindre" est l'action PRINCIPALE (~90% des
// inscriptions) : champ code au focus (autofocus natif), Entrée valide (submit natif d'un
// formulaire à un seul champ) — "Créer une chorale" reste secondaire, jamais côte à côte au
// même niveau (décision produit). Une demande en attente n'est jamais un cul-de-sac : les deux
// actions de sortie restent affichées même quand des demandes sont en cours.
//
// Focus programmatique (afterNextRender) plutôt que l'attribut HTML `autofocus`, qui dégrade
// l'accessibilité et est explicitement interdit par la règle ESLint
// @angular-eslint/template/no-autofocus.
@Component({
  selector: 'app-start',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, DataStateComponent, IconComponent, SubmitOnceDirective],
  templateUrl: './start.component.html',
  styleUrl: './start.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class StartComponent {
  private readonly onboardingService = inject(OnboardingService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  private readonly allRequests = signal<IMyRequest[]>([]);

  readonly pendingRequests = computed(() => this.allRequests().filter(d => d.Status === MembershipRequestStatusEnum.Pending));

  readonly codeForm = this.fb.nonNullable.group({
    code: this.fb.nonNullable.control('', [Validators.required])
  });

  private readonly codeInput = viewChild<ElementRef<HTMLInputElement>>('codeInput');

  constructor() {
    this.load();
    afterNextRender(() => this.codeInput()?.nativeElement.focus());
  }

  submitCode(): void {
    if (this.codeForm.invalid) {
      this.codeForm.markAllAsTouched();
      return;
    }
    const { code } = this.codeForm.getRawValue();
    this.router.navigate([`/${RoutePaths.Join}`], { queryParams: { code } });
  }

  // Fabrique une action pour SubmitOnceDirective (appSubmitOnce attend un () => Observable<unknown>
  // sans argument) — une closure par demande, capturant son id.
  cancelRequestAction(request: IMyRequest): () => Observable<unknown> {
    return () =>
      this.onboardingService
        .cancelRequest(request.Id)
        .pipe(tap(() => this.allRequests.update(list => list.filter(d => d.Id !== request.Id))));
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.onboardingService
      .mesRequests({ Page: 1, PageSize: MY_REQUESTS_PAGE_SIZE })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.loading.set(false);
          this.allRequests.set(result.Items);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger vos demandes en cours. Merci de réessayer.');
        }
      });
  }
}
