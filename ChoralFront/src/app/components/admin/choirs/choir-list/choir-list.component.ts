import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminChoirService } from '@app/services/admin/admin-choir.service';
import { ClientService } from '@app/services/admin/client.service';
import { RoutePaths } from '@core/route-paths';
import { parseEnumQueryParam, parseGuidQueryParam, parseTriStateBooleanQueryParam } from '@core/query-params.util';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  IDataTableChip,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IAdminChoirListItem } from '@models/admin-models/admin-choir-list-item.model';
import { IClient } from '@models/admin-models/client.model';
import { ChoirStatusEnum, getStatusChoirLabel } from '@app/enums/status-choir.enum';

// Le sélecteur de client charge la liste complète en une fois : l'administration compte des
// dizaines de clients, pas des milliers. Au-delà, il faudra une recherche serveur — pas une
// pagination du sélecteur, qui rendrait le filtre inutilisable.
const CLIENT_OPTIONS_PAGE_SIZE = 100;

// Liste transverse à tous les clients — l'administration générale ne crée ni n'archive de
// chorale ici sans passer par ChangeStatus (voir choir-detail.component.ts) : aucun bouton
// de création n'est exposé (décision produit `10-D23`, l'Admin ne crée jamais de chorale).
//
// Seules Nom/ClientName/Statut sont déclarées sortable=true (liste blanche stricte du contrat,
// AdminChoirsPagedFilterViewModel/AdminChoirService.ColonnesTriables) — MemberCount,
// SongCount, UpcomingEventCount et LastActivityAt sont des agrégats calculés après
// pagination côté serveur, jamais triables. CreatedAt est triable côté back mais n'est pas
// exposé par AdminChoirListItemViewModel : pas de colonne pour un champ qu'on ne peut pas
// afficher (écart assumé, pas d'invention de donnée).
@Component({
  selector: 'app-choir-list',
  standalone: true,
  imports: [DatePipe, DataTableComponent, PageHeaderComponent],
  templateUrl: './choir-list.component.html',
  styleUrl: './choir-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChoirListComponent {
  private readonly adminChoirService = inject(AdminChoirService);
  private readonly clientService = inject(ClientService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly ChoirStatusEnum = ChoirStatusEnum;
  protected readonly getStatusChoirLabel = getStatusChoirLabel;
  protected readonly allStatuss: ChoirStatusEnum[] = [
    ChoirStatusEnum.Draft,
    ChoirStatusEnum.Published,
    ChoirStatusEnum.Cancelled,
    ChoirStatusEnum.Archived
  ];

  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: IAdminChoirListItem }>>('tplStatus');
  private readonly tplLastActivity = viewChild<TemplateRef<{ $implicit: IAdminChoirListItem }>>('tplLastActivity');

  readonly items = signal<IAdminChoirListItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly filterText = signal('');

  // Valeurs initiales lues depuis les query params de la navigation (ex. tuile du tableau de
  // bord admin, voir dashboard.component.ts) — ActivatedRouteSnapshot, lecture unique au
  // chargement. Un query param absent, malformé ou hors énumération (allStatuss) retombe
  // silencieusement sur '' (voir query-params.util.ts) : jamais d'exception, jamais d'écran
  // cassé sur une URL trafiquée (OWASP A01). Ensuite, ces signaux restent des filtres normaux,
  // modifiables par l'utilisateur comme n'importe quelle autre valeur de filtre avancé.
  readonly filterClientId = signal(parseGuidQueryParam(this.route.snapshot.queryParamMap, 'ClientId') ?? '');
  readonly filterStatus = signal<ChoirStatusEnum | ''>(
    parseEnumQueryParam(this.route.snapshot.queryParamMap, 'Status', this.allStatuss) ?? ''
  );
  readonly filterInactive30j = signal(parseTriStateBooleanQueryParam(this.route.snapshot.queryParamMap, 'InactiveFor30Days'));

  // Options du sélecteur de client. L'échec de ce chargement ne casse pas l'écran : la liste
  // des chorales reste consultable, seul le filtre par client devient indisponible et le dit.
  readonly clients = signal<IClient[]>([]);
  readonly clientsLoading = signal(false);
  readonly clientsError = signal<string | null>(null);

  // Filtres avancés matérialisés en chips (§3.3). Le libellé est résolu ici — le composant
  // de tableau ne connaît ni les clients ni les statuts.
  readonly activeFilters = computed<IDataTableChip[]>(() => {
    const chips: IDataTableChip[] = [];

    const clientId = this.filterClientId();
    if (clientId) {
      const client = this.clients().find(candidate => candidate.Id === clientId);
      chips.push({ key: 'ClientId', label: `Client : ${client?.Name ?? 'sélection en cours…'}` });
    }

    const status = this.filterStatus();
    if (status !== '') {
      chips.push({ key: 'Status', label: `Statut : ${getStatusChoirLabel(status)}` });
    }

    const inactive = this.filterInactive30j();
    if (inactive !== '') {
      chips.push({
        key: 'InactiveFor30Days',
        label: inactive === 'true' ? 'Inactive depuis 30 jours' : 'Active sur 30 jours'
      });
    }

    return chips;
  });

  // computed() plutôt qu'un signal figé au constructeur : les viewChild(TemplateRef) ne sont
  // résolus qu'après la première passe de vue — un calcul unique au constructeur capturerait
  // `undefined` pour cellTemplate (voir user-list.component.ts, currentColumns).
  readonly columns = computed<IDataTableColumn<IAdminChoirListItem>[]>(() => [
    { key: 'Name', label: 'Nom', sortable: true },
    { key: 'ClientName', label: 'Client', sortable: true },
    { key: 'MemberCount', label: 'Membres' },
    { key: 'SongCount', label: 'Chants' },
    { key: 'UpcomingEventCount', label: 'Événements à venir' },
    { key: 'LastActivityAt', label: 'Dernière activité', cellTemplate: this.tplLastActivity() },
    { key: 'Status', label: 'Statut', sortable: true, cellTemplate: this.tplStatus() }
  ]);

  constructor() {
    this.load();
    this.loadClients();
  }

  private loadClients(): void {
    this.clientsLoading.set(true);

    this.clientService
      .getPaged({ Page: 1, PageSize: CLIENT_OPTIONS_PAGE_SIZE, SortActive: 'Name', SortDirection: 'asc' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.clients.set(result.Items);
          this.clientsLoading.set(false);
        },
        error: () => {
          this.clientsLoading.set(false);
          this.clientsError.set('Liste des clients indisponible — filtre par client désactivé.');
        }
      });
  }

  onFilterChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.load();
  }

  onSortChange(event: { active: string; direction: 'asc' | 'desc' }): void {
    this.sortActive.set(event.active);
    this.sortDirection.set(event.direction);
    this.load();
  }

  onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  onAdvancedFilterChange(): void {
    this.page.set(1);
    this.load();
  }

  onClientChange(value: string): void {
    this.filterClientId.set(value);
    this.onAdvancedFilterChange();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  // Retrait d'un filtre depuis sa chip. La clé correspond au champ du contrat de filtre —
  // un `default` silencieux masquerait une clé inconnue au lieu de la signaler au build.
  onFilterRemove(key: string): void {
    switch (key) {
      case 'ClientId':
        this.filterClientId.set('');
        break;
      case 'Status':
        this.filterStatus.set('');
        break;
      case 'InactiveFor30Days':
        this.filterInactive30j.set('');
        break;
      default:
        return;
    }

    this.onAdvancedFilterChange();
  }

  onStatusChange(value: string): void {
    this.filterStatus.set(value === '' ? '' : (Number(value) as ChoirStatusEnum));
    this.onAdvancedFilterChange();
  }

  onInactive30jChange(value: string): void {
    this.filterInactive30j.set(value as '' | 'true' | 'false');
    this.onAdvancedFilterChange();
  }

  onRowClick(row: IAdminChoirListItem): void {
    this.router.navigate(['/', RoutePaths.Admin, RoutePaths.AdminChoirs, row.Id]);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const inactive30j = this.filterInactive30j();
    const status = this.filterStatus();

    this.adminChoirService
      .getPaged(
        {
          Page: this.page(),
          PageSize: this.pageSize(),
          SortActive: this.sortActive(),
          SortDirection: this.sortDirection(),
          Filter: this.filterText() || undefined
        },
        {
          ClientId: this.filterClientId() || undefined,
          Status: status === '' ? undefined : status,
          InactiveFor30Days: inactive30j === '' ? undefined : inactive30j === 'true'
        }
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.items.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les chorales. Merci de réessayer.');
        }
      });
  }
}
