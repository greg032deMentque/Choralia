import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminAuditService } from '@app/services/admin/admin-audit.service';
import { DataTableComponent, DEFAULT_PAGE_SIZE, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IAdminAuditLogListItem } from '@models/admin-models/admin-audit-log.model';

const INVALID_PERIOD_MESSAGE = 'Période invalide : la date de début est postérieure à la date de fin.';

// Écran d'audit de l'administration générale (`10-D30`) — LECTURE SEULE volontaire : un
// journal d'audit modifiable ne vaut rien, aucune action d'écriture n'est rendue ici (voir
// AdminAuditController, aucune route de mutation exposée côté back).
//
// Seules OccurredAt/Action/EntityType sont déclarées sortable=true — liste blanche stricte du
// serveur (AdminAuditListService.AuditColonnesTriables) : un en-tête cliquable qui ne trie rien
// est le défaut qu'on vient de corriger sur l'ensemble du projet, on ne le réintroduit pas ici.
//
// Pas de recherche texte libre : AdminAuditListService.GetPagedAsync n'utilise jamais
// filter.Filter (seuls UserId/EntityType/Action/StartDate/EndDate sont exploités) — la barre de
// recherche intégrée de DataTableComponent est donc désactivée ([filterable]=false) plutôt que
// de laisser un champ qui n'aurait aucun effet côté serveur.
//
// Période inversée (StartDate > EndDate) : le back (AdminAuditListService) renvoie une page
// vide dans ce cas, indiscernable d'un « aucun résultat ». Ce composant détecte l'inversion
// AVANT d'appeler le serveur et affiche un message dédié à la place — jamais le message
// générique « aucun résultat pour ce filtre », qui laisserait croire que la période est
// simplement sans activité.
@Component({
  selector: 'app-admin-audit',
  standalone: true,
  imports: [DatePipe, DataTableComponent, PageHeaderComponent],
  templateUrl: './audit.component.html',
  styleUrl: './audit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminAuditComponent {
  private readonly adminAuditService = inject(AdminAuditService);
  private readonly destroyRef = inject(DestroyRef);

  private readonly tplDate = viewChild<TemplateRef<{ $implicit: IAdminAuditLogListItem }>>('tplDate');
  private readonly tplActeur = viewChild<TemplateRef<{ $implicit: IAdminAuditLogListItem }>>('tplActeur');
  private readonly tplEntityId = viewChild<TemplateRef<{ $implicit: IAdminAuditLogListItem }>>('tplEntityId');
  private readonly tplDetail = viewChild<TemplateRef<{ $implicit: IAdminAuditLogListItem }>>('tplDetail');

  readonly columns = computed<IDataTableColumn<IAdminAuditLogListItem>[]>(() => [
    { key: 'OccurredAt', label: 'Date', sortable: true, cellTemplate: this.tplDate() },
    { key: 'UserFullName', label: 'Acteur', cellTemplate: this.tplActeur() },
    { key: 'Action', label: 'Action', sortable: true },
    { key: 'EntityType', label: "Type d'entité", sortable: true },
    { key: 'EntityId', label: "Identifiant d'entité", cellTemplate: this.tplEntityId() },
    { key: 'Detail', label: 'Détail', cellTemplate: this.tplDetail() }
  ]);

  readonly items = signal<IAdminAuditLogListItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly filterUserId = signal('');
  readonly filterEntityType = signal('');
  readonly filterAction = signal('');
  // Bornes brutes d'<input type="date"> (yyyy-MM-dd) — converties en horodatage de début/fin
  // de journée uniquement au moment de l'appel (voir buildDateBounds).
  readonly filterStartDate = signal('');
  readonly filterEndDate = signal('');

  constructor() {
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

  onUserIdChange(value: string): void {
    this.filterUserId.set(value);
    this.onAdvancedFilterChange();
  }

  onEntityTypeChange(value: string): void {
    this.filterEntityType.set(value);
    this.onAdvancedFilterChange();
  }

  onActionChange(value: string): void {
    this.filterAction.set(value);
    this.onAdvancedFilterChange();
  }

  onStartDateChange(value: string): void {
    this.filterStartDate.set(value);
    this.onAdvancedFilterChange();
  }

  onEndDateChange(value: string): void {
    this.filterEndDate.set(value);
    this.onAdvancedFilterChange();
  }

  private load(): void {
    const debut = this.filterStartDate();
    const fin = this.filterEndDate();

    // Comparaison lexicale valide : format yyyy-MM-dd, l'ordre des chaînes suit l'ordre
    // chronologique. Détecté AVANT tout appel réseau — voir commentaire de classe.
    if (debut !== '' && fin !== '' && debut > fin) {
      this.loading.set(false);
      this.items.set([]);
      this.totalCount.set(0);
      this.error.set(INVALID_PERIOD_MESSAGE);
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.adminAuditService
      .getPaged(
        { Page: this.page(), PageSize: this.pageSize(), SortActive: this.sortActive(), SortDirection: this.sortDirection() },
        {
          UserId: this.filterUserId() || undefined,
          EntityType: this.filterEntityType() || undefined,
          Action: this.filterAction() || undefined,
          StartDate: debut ? `${debut}T00:00:00` : undefined,
          EndDate: fin ? `${fin}T23:59:59` : undefined
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
          this.error.set("Impossible de charger le journal d'audit. Merci de réessayer.");
        }
      });
  }
}
