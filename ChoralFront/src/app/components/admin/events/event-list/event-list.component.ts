import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminEventService } from '@app/services/admin/admin-event.service';
import { RoutePaths } from '@core/route-paths';
import { parseEnumQueryParam, parseGuidQueryParam, parseTriStateBooleanQueryParam } from '@core/query-params.util';
import { DataTableComponent, DEFAULT_PAGE_SIZE, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminEventListItem } from '@models/admin-models/admin-event-list-item.model';
import { EventStatusEnum, getEventStatusLabel } from '@app/enums/event-status.enum';
import { EventTypeEnum, getEventTypeLabel } from '@app/enums/event-type.enum';


// Liste transverse à tous les clients, lecture seule (aucune écriture exposée par
// AdminEvenementController — voir EventService pour la management réelle côté chorale).
//
// DataTableComponent (figé) ne permet pas de classe CSS conditionnelle sur <tr> : la mise en
// évidence d'un événement autonome orphelin (IsTechnicalClientAnomaly) passe donc par une
// colonne dédiée avec badge d'alerte plutôt qu'une surcharge de ligne — seul levier disponible
// sans update le composant partagé.
@Component({
  selector: 'app-event-list',
  standalone: true,
  imports: [DatePipe, DataTableComponent, PageHeaderComponent, IconComponent],
  templateUrl: './event-list.component.html',
  styleUrl: './event-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventListComponent {
  private readonly adminEventService = inject(AdminEventService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly EventStatusEnum = EventStatusEnum;
  protected readonly getEventStatusLabel = getEventStatusLabel;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly allStatuss: EventStatusEnum[] = [
    EventStatusEnum.Draft,
    EventStatusEnum.Published,
    EventStatusEnum.Cancelled,
    EventStatusEnum.Archived
  ];
  protected readonly allTypes: EventTypeEnum[] = [
    EventTypeEnum.Concert,
    EventTypeEnum.Rehearsal,
    EventTypeEnum.Wedding,
    EventTypeEnum.Mass,
    EventTypeEnum.Funeral,
    EventTypeEnum.Other
  ];

  private readonly tplDate = viewChild<TemplateRef<{ $implicit: IAdminEventListItem }>>('tplDate');
  private readonly tplChoir = viewChild<TemplateRef<{ $implicit: IAdminEventListItem }>>('tplChorale');
  private readonly tplType = viewChild<TemplateRef<{ $implicit: IAdminEventListItem }>>('tplType');
  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: IAdminEventListItem }>>('tplStatus');
  private readonly tplAnomalie = viewChild<TemplateRef<{ $implicit: IAdminEventListItem }>>('tplAnomalie');

  readonly columns = computed<IDataTableColumn<IAdminEventListItem>[]>(() => [
    { key: 'Title', label: 'Titre', sortable: true },
    { key: 'StartDate', label: 'Date', sortable: true, cellTemplate: this.tplDate() },
    { key: 'ChoirName', label: 'Chorale', sortable: true, cellTemplate: this.tplChoir() },
    { key: 'ClientName', label: 'Client', sortable: true },
    { key: 'Type', label: 'Type', sortable: true, cellTemplate: this.tplType() },
    { key: 'Status', label: 'Statut', sortable: true, cellTemplate: this.tplStatus() },
    { key: 'ParticipantCount', label: 'Participants' },
    { key: 'IsTechnicalClientAnomaly', label: 'Anomalie', cellTemplate: this.tplAnomalie() }
  ]);

  readonly items = signal<IAdminEventListItem[]>([]);
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
  // chargement. Un query param absent, malformé ou hors énumération (allStatuss/allTypes)
  // retombe silencieusement sur '' (voir query-params.util.ts) : jamais d'exception, jamais
  // d'écran cassé sur une URL trafiquée (OWASP A01). Ensuite, ces signaux restent des filtres
  // normaux, modifiables par l'utilisateur comme n'importe quelle autre valeur de filtre avancé.
  readonly filterClientId = signal(parseGuidQueryParam(this.route.snapshot.queryParamMap, 'ClientId') ?? '');
  readonly filterChoirId = signal(parseGuidQueryParam(this.route.snapshot.queryParamMap, 'ChoirId') ?? '');
  readonly filterStatus = signal<EventStatusEnum | ''>(
    parseEnumQueryParam(this.route.snapshot.queryParamMap, 'Status', this.allStatuss) ?? ''
  );
  readonly filterType = signal<EventTypeEnum | ''>(
    parseEnumQueryParam(this.route.snapshot.queryParamMap, 'Type', this.allTypes) ?? ''
  );
  readonly filterUpcoming = signal(parseTriStateBooleanQueryParam(this.route.snapshot.queryParamMap, 'Upcoming'));

  constructor() {
    this.load();
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

  // Revient à la première page : rester sur la page 7 après être passé de 25 à 100 lignes
  // afficherait un écart de données que rien ne signale à l'écran.
  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  onAdvancedFilterChange(): void {
    this.page.set(1);
    this.load();
  }

  onClientIdChange(value: string): void {
    this.filterClientId.set(value);
    this.onAdvancedFilterChange();
  }

  onChoirIdChange(value: string): void {
    this.filterChoirId.set(value);
    this.onAdvancedFilterChange();
  }

  onStatusChange(value: string): void {
    this.filterStatus.set(value === '' ? '' : (Number(value) as EventStatusEnum));
    this.onAdvancedFilterChange();
  }

  onTypeChange(value: string): void {
    this.filterType.set(value === '' ? '' : (Number(value) as EventTypeEnum));
    this.onAdvancedFilterChange();
  }

  onUpcomingChange(value: string): void {
    this.filterUpcoming.set(value as '' | 'true' | 'false');
    this.onAdvancedFilterChange();
  }

  onRowClick(row: IAdminEventListItem): void {
    this.router.navigate(['/', RoutePaths.Admin, RoutePaths.AdminEvents, row.Id]);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const status = this.filterStatus();
    const type = this.filterType();
    const upcoming = this.filterUpcoming();

    this.adminEventService
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
          ChoirId: this.filterChoirId() || undefined,
          Status: status === '' ? undefined : status,
          Type: type === '' ? undefined : type,
          Upcoming: upcoming === '' ? undefined : upcoming === 'true'
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
          this.error.set('Impossible de charger les événements. Merci de réessayer.');
        }
      });
  }
}
