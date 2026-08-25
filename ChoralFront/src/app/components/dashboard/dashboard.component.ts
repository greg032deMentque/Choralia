import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { AuthStore } from '@core/auth.store';
import { DashboardService } from '@app/services/dashboard/dashboard.service';
import { IChoirKpi, IDashboardKpi, INextEvent } from '@models/common-models/dashboard-summary.model';

// Tableau de bord de l'espace actif. Toutes les valeurs affichées viennent d'un appel
// réel (D30) : aucune tuile n'est rendue tant que les données ne sont pas chargées, plutôt
// qu'un zéro qui serait pris pour une mesure.
//
// Le panneau « Activité récente » a été retiré : il n'existe aucun flux d'audit lisible
// pour l'alimenter. Un écran vide en permanence est une impasse — il reviendra avec sa
// source.
//
// GET /api/dashboard/ChoirKpi n'existe que pour un espace de type Chorale (il interroge le
// répertoire/les sections de LA chorale du scope) : appelé sur un espace Événement, il
// renvoie 403. « Événements à venir » n'a pas de source indépendante — il est dérivé de ce
// même ChoirKpiViewModel.UpcomingEvents — donc tout le bloc (tuiles + événements à venir)
// est absent pour un espace Événement, plutôt qu'un écran d'erreur permanent. Aucun
// indicateur de repli fabriqué (D30) : DashboardController n'expose aucun endpoint
// équivalent pour un espace Événement.
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [IconComponent, DataStateComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardComponent {
  private readonly dashboardService = inject(DashboardService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;

  protected readonly isChoirSpace = computed(() => this.authStore.activeSpaceType() === SpaceTypeEnum.Choir);

  readonly kpi = signal<IChoirKpi | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly kpis = computed<IDashboardKpi[]>(() => {
    const data = this.kpi();
    if (data === null) {
      return [];
    }

    return [
      {
        Label: 'Chants au répertoire',
        Value: String(data.SongsInRepertoire),
        Icon: IconNameEnum.MusicNotes
      },
      {
        // Un ratio plutôt qu'un nombre nu : « 3 » ne dit pas si c'est beaucoup.
        Label: 'Chants incomplets',
        Value: `${data.IncompleteSongs} sur ${data.SongsInRepertoire}`,
        Icon: IconNameEnum.MusicNotes
      },
      {
        Label: 'Enregistrements à valider',
        Value: String(data.RecordingsPendingReview),
        Icon: IconNameEnum.FileMusic
      },
      {
        Label: data.InvitedMembers > 0 ? 'Membres · dont invités' : 'Membres',
        Value:
          data.InvitedMembers > 0
            ? `${data.Members} · ${data.InvitedMembers}`
            : String(data.Members),
        Icon: IconNameEnum.Users
      }
    ];
  });

  readonly prochainsEvents = computed<INextEvent[]>(
    () => this.kpi()?.UpcomingEvents ?? []
  );

  constructor() {
    if (this.isChoirSpace()) {
      this.load();
    }
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.dashboardService
      .getChoirKpi()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: kpi => {
          this.kpi.set(kpi);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Impossible de charger les indicateurs.');
          this.loading.set(false);
        }
      });
  }
}
