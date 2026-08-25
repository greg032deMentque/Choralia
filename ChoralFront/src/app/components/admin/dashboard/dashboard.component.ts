import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { AdminDashboardService } from '@app/services/admin/admin-dashboard.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { RoutePaths } from '@core/route-paths';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminDashboardKpi } from '@models/admin-models/admin-dashboard-kpi.model';
import { IPurgeCandidates } from '@models/admin-models/admin-guest-purge.model';
import { ClientStatusEnum } from '@app/enums/status-client.enum';
import { ChoirStatusEnum } from '@app/enums/status-choir.enum';
import { formatBytes } from '@app/services/admin/format-bytes.util';

// Une tuile de KPI. `value === 0` => jamais cliquable (Q24 : une tuile à 0 n'est pas une
// action, la rendre cliquable inviterait à cliquer sur du vide). `onClick` absent => tuile
// purement informationnelle (ex. stockage total, non actionnable par décision produit — voir
// AdminDashboardKpiViewModel.TotalStorageBytes côté back).
export interface IKpiTile {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly displayValue: string;
  readonly clickable: boolean;
  readonly variant: 'normal' | 'anomalie';
  readonly onClick?: () => void;
}

export interface IKpiSection {
  readonly title: string;
  readonly tiles: IKpiTile[];
}

// Panneau dépliant pour les deux KPI qui n'exposent qu'une liste d'identifiants (ClientIds),
// sans filtre serveur possible — voir NotStartedClients/ClientsNearCap ci-dessous.
export type ExpandableClientsPanel = 'non-demarres' | 'proches-plafond' | null;

// Tableau de bord de l'administration générale (`10-D30`). Toutes les tuiles viennent d'un
// appel réel (GetKpi) : aucune tuile décorative, aucun indicateur financier inventé (D30).
//
// Chargement partiel (exigence explicite du lot) : le KPI (GetKpi) et l'aperçu de purge RGPD
// (GetPurgeCandidates, bloc distinct ci-dessous) sont DEUX appels HTTP indépendants, chacun
// avec son propre couple loading/error. Gabarit : jamais un seul @if englobant qui masquerait
// tout l'écran sur l'échec de l'un des deux — un échec du KPI n'empêche pas d'utiliser le bloc
// de purge, et réciproquement.
//
// Emplacement du bloc de purge RGPD : ce tableau de bord (pas l'écran d'audit) — c'est l'écran
// d'actions de l'administration générale, l'audit reste volontairement lecture seule (voir
// audit.component.ts). AdminDashboardService porte donc aussi GetPurgeCandidates/PurgeInactive
// (routes admin-guest-accounts côté back) plutôt que de créer un 3ᵉ fichier de service hors du
// périmètre du lot.
@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [RouterLink, DataStateComponent, PageHeaderComponent, IconComponent, SubmitOnceDirective],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminDashboardComponent {
  private readonly adminDashboardService = inject(AdminDashboardService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly formatBytes = formatBytes;
  protected readonly RoutePaths = RoutePaths;

  // --- KPI (GetKpi)
  readonly kpi = signal<IAdminDashboardKpi | null>(null);
  readonly kpiLoading = signal(false);
  readonly kpiError = signal<string | null>(null);

  readonly expandedPanel = signal<ExpandableClientsPanel>(null);

  // --- Purge RGPD (bloc indépendant, voir en-tête de classe)
  readonly purgePreview = signal<IPurgeCandidates | null>(null);
  readonly purgePreviewLoading = signal(false);
  readonly purgePreviewError = signal<string | null>(null);
  readonly purgeResult = signal<number | null>(null);

  readonly storageTile = computed<IKpiTile | null>(() => {
    const data = this.kpi();
    if (!data) return null;
    return {
      key: 'storage-total',
      label: 'Stockage total consommé',
      value: data.TotalStorageBytes,
      displayValue: formatBytes(data.TotalStorageBytes),
      clickable: false,
      variant: 'normal'
    };
  });

  readonly anomalieTile = computed<IKpiTile | null>(() => {
    const data = this.kpi();
    if (!data) return null;
    return this.tile(
      'anomalie-events',
      'Événements sans structure — à rattacher',
      data.EventsWithoutStructureAnomaly.Count,
      () => this.navigate(RoutePaths.AdminEvents),
      'anomalie'
    );
  });

  readonly sections = computed<IKpiSection[]>(() => {
    const data = this.kpi();
    if (!data) return [];

    return [
      {
        title: 'Clients',
        tiles: [
          this.tile('clients-total', 'Total', data.Clients.Total, () => this.navigate(RoutePaths.AdminClients)),
          this.tile('clients-actifs', 'Actifs', data.Clients.Active, () =>
            this.navigate(RoutePaths.AdminClients, { Status: ClientStatusEnum.Active })
          ),
          this.tile('clients-suspendus', 'Suspendus', data.Clients.Suspended, () =>
            this.navigate(RoutePaths.AdminClients, { Status: ClientStatusEnum.Suspended })
          ),
          this.tile('clients-archives', 'Archivés', data.Clients.Archived, () =>
            this.navigate(RoutePaths.AdminClients, { Status: ClientStatusEnum.Archived })
          )
        ]
      },
      {
        title: 'Chorales',
        tiles: [
          this.tile('choirs-total', 'Total', data.Choirs.Total, () => this.navigate(RoutePaths.AdminChoirs)),
          this.tile('choirs-brouillon', 'Brouillon', data.Choirs.Draft, () =>
            this.navigate(RoutePaths.AdminChoirs, { Status: ChoirStatusEnum.Draft })
          ),
          this.tile('choirs-publiees', 'Publiées', data.Choirs.Published, () =>
            this.navigate(RoutePaths.AdminChoirs, { Status: ChoirStatusEnum.Published })
          ),
          this.tile('choirs-annulees', 'Annulées', data.Choirs.Cancelled, () =>
            this.navigate(RoutePaths.AdminChoirs, { Status: ChoirStatusEnum.Cancelled })
          ),
          this.tile('choirs-archivees', 'Archivées', data.Choirs.Archived, () =>
            this.navigate(RoutePaths.AdminChoirs, { Status: ChoirStatusEnum.Archived })
          ),
          this.tile('choirs-inactives', 'Inactives depuis 30 jours', data.InactiveChoirs.Count, () =>
            this.navigate(RoutePaths.AdminChoirs, { InactiveFor30Days: true })
          )
        ]
      },
      {
        // Écart assumé (voir IUtilisateursKpi côté modèle) : AdminUserController n'accepte
        // aujourd'hui aucun filtre IsActive/IsGuestAccount — la navigation transmet quand même
        // ces query params par anticipation d'un futur ajout côté serveur, sans qu'ils aient
        // d'effet réel tant que ce filtre n'existe pas.
        title: 'Utilisateurs',
        tiles: [
          this.tile('users-total', 'Total', data.Users.Total, () => this.navigate(RoutePaths.AdminUsers)),
          this.tile('users-actifs', 'Actifs', data.Users.Active, () =>
            this.navigate(RoutePaths.AdminUsers, { IsActive: true })
          ),
          this.tile('users-invites', 'Invités non activés', data.Users.InactiveInvitees, () =>
            this.navigate(RoutePaths.AdminUsers, { IsGuestAccount: true })
          )
        ]
      },
      {
        title: 'Chants',
        tiles: [
          this.tile('chants-total', 'Total au catalogue', data.Songs.Total, () => this.navigate(RoutePaths.AdminSongs)),
          this.tile('chants-doublons', 'Groupes en doublon', data.Songs.DuplicateGroups, () =>
            this.navigate(RoutePaths.AdminSongs, { DuplicatesOnly: true })
          )
        ]
      },
      {
        title: 'Suivi',
        tiles: [
          this.tile('events-a-venir', 'Événements à venir (30 j)', data.UpcomingEvents30Days, () =>
            this.navigate(RoutePaths.AdminEvents, { Upcoming: true })
          )
        ]
      }
    ];
  });

  // Panneaux dépliants (NotStartedClients / ClientsNearCap) : aucun filtre serveur
  // possible aujourd'hui (ClientController.GetPaged n'accepte aucun filtre — voir écart assumé
  // dans le modèle) — seule la liste d'identifiants transmise par le KPI est exploitable, sous
  // forme de liens directs vers chaque fiche client (route /admin/clients/:id, déjà livrée).
  readonly clientsNonDemarresIds = computed(() => this.kpi()?.NotStartedClients.ClientIds ?? []);
  readonly clientsNearCapIds = computed(() => this.kpi()?.ClientsNearCap.ClientIds ?? []);

  constructor() {
    this.loadKpi();
  }

  loadKpi(): void {
    this.kpiLoading.set(true);
    this.kpiError.set(null);

    this.adminDashboardService
      .getKpi()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: kpi => {
          this.kpi.set(kpi);
          this.kpiLoading.set(false);
        },
        error: () => {
          this.kpiLoading.set(false);
          this.kpiError.set('Impossible de charger les indicateurs. Merci de réessayer.');
        }
      });
  }

  toggleClientsPanel(panel: Exclude<ExpandableClientsPanel, null>): void {
    this.expandedPanel.set(this.expandedPanel() === panel ? null : panel);
  }

  // --- Purge RGPD
  previewPurge(): void {
    this.purgePreviewLoading.set(true);
    this.purgePreviewError.set(null);
    this.purgeResult.set(null);

    this.adminDashboardService
      .getPurgeCandidates()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: preview => {
          this.purgePreview.set(preview);
          this.purgePreviewLoading.set(false);
        },
        error: () => {
          this.purgePreviewLoading.set(false);
          this.purgePreviewError.set("Impossible de charger l'aperçu de purge. Merci de réessayer.");
        }
      });
  }

  // Bouton sous appSubmitOnce. Le nombre affiché après succès est TOUJOURS celui retourné par
  // PurgeInactive (AnonymizedCount), jamais celui de l'aperçu — un compte revendiqué entre
  // l'aperçu et l'action n'est pas purgé (voir IPurgeGuestsResult).
  purgeAction = (): Observable<unknown> => {
    const preview = this.purgePreview();
    if (!preview || preview.Count === 0) return throwError(() => new Error('no-preview'));

    return from(
      this.confirmService.confirm({
        title: 'Purger les comptes invités inactives ?',
        message: 'Cette action anonymise définitivement les comptes invités inactives identifiés par l’aperçu.',
        impacts: [`${preview.Count} compte(s) invité(s) concerné(s)`],
        danger: true,
        confirmationLabel: 'Purger'
      })
    ).pipe(
      switchMap(confirmed => (confirmed ? this.adminDashboardService.purgeInactive() : throwError(() => new Error('cancelled')))),
      tap(result => {
        this.purgeResult.set(result.AnonymizedCount);
        this.purgePreview.set(null);
        this.toastService.success(`${result.AnonymizedCount} compte(s) invité(s) purgé(s).`);
      }),
      catchError((err: unknown) => {
        // Annulation utilisateur : pas d'erreur à remonter, pas de toast (déjà géré par
        // ApiErrorInterceptor pour toute vraie erreur HTTP — voir CLAUDE.md, responsabilité
        // intercepteur = toast global, composant = état inline).
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  private tile(
    key: string,
    label: string,
    value: number,
    onClick: () => void,
    variant: 'normal' | 'anomalie' = 'normal'
  ): IKpiTile {
    return { key, label, value, displayValue: String(value), clickable: value > 0, variant, onClick };
  }

  private navigate(segment: string, queryParams?: Record<string, string | number | boolean>): void {
    void this.router.navigate(['/', RoutePaths.Admin, segment], queryParams ? { queryParams } : {});
  }
}
