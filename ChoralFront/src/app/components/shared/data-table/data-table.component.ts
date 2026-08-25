import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal, TemplateRef } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { debounce } from '@core/debounce.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

const FILTER_DEBOUNCE_MS = 300;

// Pagination par défaut (Spec §3.3). Partagée par toutes les listes plutôt que redéclarée
// écran par écran : une liste qui pagine différemment des autres est une incohérence, pas
// une préférence locale.
export const DEFAULT_PAGE_SIZE = 25;
export const PAGE_SIZE_OPTIONS: readonly number[] = [25, 50, 100];

// Gabarit personnalisé optionnel pour une colonne : le composant appelant fournit un
// <ng-template #xxx let-item> et référence son TemplateRef (via @ViewChild, résolu en
// AfterViewInit) dans la config de colonne correspondante.
export interface IDataTableColumn<T> {
  readonly key: string;
  readonly label: string;
  readonly sortable?: boolean;
  readonly cellTemplate?: TemplateRef<{ $implicit: T }>;
  // Masque la colonne en rendu carte (sous tablette) : réservé aux informations de confort
  // qui feraient de la carte un pavé illisible. Jamais une donnée nécessaire à la décision.
  readonly hideOnMobile?: boolean;
}

// Filtre actif matérialisé sous forme de chip supprimable (Spec §3.3). Le parent reste
// propriétaire de la valeur : le composant n'émet que la demande de retrait.
export interface IDataTableChip {
  readonly key: string;
  readonly label: string;
}

// Fonction de regroupement optionnelle (voir input `groupBy`) : appliquée uniquement à la
// page déjà reçue via `items()` (aucun appel réseau supplémentaire), ne rompt donc jamais la
// pagination/le tri serveur. `key` identifie le groupe (ex. ChoirId), `label` est déjà résolu
// par le parent — comme pour `IDataTableChip`, ce composant n'a aucune connaissance du domaine.
export type DataTableGroupByFn<T> = (item: T) => { key: string; label: string };

type DataTableRenderRow<T> = { kind: 'group-header'; key: string; label: string; count: number } | { kind: 'row'; item: T };

// Tableau générique réutilisable (lists paginées : users, clients, chorales,
// événements, catalogue de chants...). Délègue chargement/erreur/vide à DataStateComponent,
// distingue "aucune donnée" et "aucun résultat pour ce filtre" (un utilisateur qui filtre ne
// doit pas croire que sa liste entière est vide), et recule automatiquement d'une page quand
// la page courante devient vide après une suppression (dernier élément de la dernière page).
// Le parent reste propriétaire du chargement réel : ce composant émet des événements
// (sortChange/pageChange/filterChange/pageSizeChange), il ne fait aucun appel HTTP lui-même.
//
// Responsive (Spec §0) : sous 768 px, le même DOM se rend en cartes — une ligne devient une
// carte, chaque cellule affiche son libellé de colonne via `data-label`. Un seul gabarit,
// jamais deux templates à maintenir en parallèle. L'en-tête de tri disparaissant avec le
// tableau, un sélecteur de tri dédié le remplace à ce format.
@Component({
  selector: 'app-data-table',
  standalone: true,
  imports: [PaginationComponent, NgTemplateOutlet, DataStateComponent, IconComponent],
  templateUrl: './data-table.component.html',
  styleUrl: './data-table.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DataTableComponent<T> {
  readonly columns = input.required<IDataTableColumn<T>[]>();
  readonly items = input.required<T[]>();
  readonly totalCount = input<number>(0);
  readonly page = input<number>(1);
  readonly pageSize = input<number>(DEFAULT_PAGE_SIZE);
  readonly sortActive = input<string | undefined>(undefined);
  readonly sortDirection = input<'asc' | 'desc' | undefined>(undefined);
  readonly loading = input<boolean>(false);
  readonly error = input<string | null>(null);
  readonly filterable = input<boolean>(true);
  readonly rowClickable = input<boolean>(false);
  readonly emptyMessage = input<string>('Aucune donnée pour le moment.');
  readonly noResultsMessage = input<string>('Aucun résultat pour ce filtre.');
  readonly emptyIcon = input<IconNameEnum>(IconNameEnum.MagnifyingGlass);
  // Action principale de l'état vide. Masquée dès qu'un filtre est actif : proposer de créer
  // un élément alors que la liste est simplement filtrée est un contresens.
  readonly emptyActionLabel = input<string | null>(null);
  readonly retryLabel = input<string | null>(null);

  // Chips des filtres avancés actifs. Le libellé est déjà résolu par le parent (« Client :
  // Chœur de la Recette ») — le composant n'a aucune connaissance du domaine.
  readonly activeFilters = input<IDataTableChip[]>([]);

  // Active le panneau de filtres repliable, alimenté par projection de contenu
  // (`<div data-table-filters>…</div>`). Sans contenu projeté, laisser à false.
  readonly filterPanel = input<boolean>(false);

  // Le sélecteur de taille de page n'apparaît que si le parent traite `pageSizeChange` :
  // un contrôle sans effet est pire que pas de contrôle.
  readonly pageSizeSelectable = input<boolean>(false);
  readonly pageSizeOptions = input<readonly number[]>(PAGE_SIZE_OPTIONS);

  // Regroupement client de la page affichée (ex. utilisateurs par chorale). `null` par défaut :
  // comportement strictement inchangé pour tout consommateur existant qui ne le renseigne pas.
  readonly groupBy = input<DataTableGroupByFn<T> | null>(null);

  readonly sortChange = output<{ active: string; direction: 'asc' | 'desc' }>();
  readonly pageChange = output<number>();
  readonly pageSizeChange = output<number>();
  readonly filterChange = output<string>();
  readonly filterRemove = output<string>();
  readonly rowClick = output<T>();
  readonly emptyAction = output();
  readonly retry = output();

  protected readonly IconNameEnum = IconNameEnum;

  protected readonly filterText = signal('');
  protected readonly filterPanelOpen = signal(false);

  protected readonly hasActiveFilter = computed(() => this.filterText().trim().length > 0 || this.activeFilters().length > 0);

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  protected readonly sortableColumns = computed(() => this.columns().filter(column => column.sortable));

  protected readonly emptyMessageResolved = computed(() =>
    this.hasActiveFilter() ? this.noResultsMessage() : this.emptyMessage()
  );

  protected readonly emptyActionLabelResolved = computed(() => (this.hasActiveFilter() ? null : this.emptyActionLabel()));

  protected readonly emptyIconResolved = computed(() =>
    this.hasActiveFilter() ? IconNameEnum.MagnifyingGlass : this.emptyIcon()
  );

  // Liste à plat, groupes intercalés — un seul `@for` en gabarit couvre les deux modes (groupé
  // ou non), sans dupliquer le rendu de ligne. Tri des groupes : alphabétique sur le libellé
  // (Spec, ordre déterministe indépendant de l'ordre de retour du serveur).
  protected readonly renderRows = computed<DataTableRenderRow<T>[]>(() => {
    const groupByFn = this.groupBy();
    if (!groupByFn) {
      return this.items().map(item => ({ kind: 'row' as const, item }));
    }

    const groups = new Map<string, { label: string; items: T[] }>();
    for (const item of this.items()) {
      const { key, label } = groupByFn(item);
      const existing = groups.get(key);
      if (existing) {
        existing.items.push(item);
      } else {
        groups.set(key, { label, items: [item] });
      }
    }

    return Array.from(groups.entries())
      .sort(([, a], [, b]) => a.label.localeCompare(b.label, 'fr'))
      .flatMap(([key, group]) => [
        { kind: 'group-header' as const, key, label: group.label, count: group.items.length },
        ...group.items.map(item => ({ kind: 'row' as const, item }))
      ]);
  });

  // Anti-rebond (300 ms) sur le filtre texte — cf. core/debounce.util (pas de Subject RxJS).
  private readonly debouncedEmitFilter = debounce((value: string) => this.filterChange.emit(value), FILTER_DEBOUNCE_MS);

  constructor() {
    // Dernière page devenue vide (ex. suppression du dernier élément) : recule d'une page
    // automatiquement, sauf si l'absence de résultat vient d'un filtre actif (ce n'est pas
    // le même cas — le parent doit rester sur place et laisser l'utilisateur ajuster son filtre).
    effect(() => {
      if (this.loading() || this.error()) return;
      if (this.page() > 1 && this.items().length === 0 && !this.hasActiveFilter()) {
        this.pageChange.emit(this.page() - 1);
      }
    });
  }

  onFilterInput(value: string): void {
    this.filterText.set(value);
    this.debouncedEmitFilter(value);
  }

  toggleFilterPanel(): void {
    this.filterPanelOpen.update(open => !open);
  }

  onFilterRemove(key: string): void {
    this.filterRemove.emit(key);
  }

  onSort(column: IDataTableColumn<T>): void {
    if (!column.sortable) return;

    if (this.sortActive() === column.key) {
      this.sortChange.emit({ active: column.key, direction: this.sortDirection() === 'asc' ? 'desc' : 'asc' });
    } else {
      this.sortChange.emit({ active: column.key, direction: 'asc' });
    }
  }

  // Sélecteur de tri du rendu carte : l'en-tête de colonne n'existe plus à ce format.
  onSortKeyChange(key: string): void {
    const column = this.sortableColumns().find(candidate => candidate.key === key);
    if (!column) return;
    this.sortChange.emit({ active: column.key, direction: this.sortDirection() ?? 'asc' });
  }

  toggleSortDirection(): void {
    const active = this.sortActive() ?? this.sortableColumns()[0]?.key;
    if (!active) return;
    this.sortChange.emit({ active, direction: this.sortDirection() === 'asc' ? 'desc' : 'asc' });
  }

  onRowClick(item: T): void {
    if (this.rowClickable()) {
      this.rowClick.emit(item);
    }
  }

  onEmptyAction(): void {
    this.emptyAction.emit();
  }

  onRetry(): void {
    this.retry.emit();
  }

  onPageSizeChange(value: string): void {
    const size = Number(value);
    if (!Number.isFinite(size) || size <= 0) return;
    this.pageSizeChange.emit(size);
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.pageChange.emit(page);
  }

  protected getCellValue(row: T, key: string): string | number | boolean | null {
    const value = (row as Record<string, unknown>)[key];
    if (value === null || value === undefined) return null;
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return value;
    return String(value);
  }
}
