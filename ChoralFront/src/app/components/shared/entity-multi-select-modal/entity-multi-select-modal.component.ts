import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, effect, inject, input, output, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { DataTableComponent, DEFAULT_PAGE_SIZE, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { ISelectOption } from '@models/common-models/select-option.model';
import { IPaginatedResult, IPaginationQueryParams } from '@models/common-models/paginated-result.model';

// Fonction de recherche paginée fournie par l'appelant — le texte libre voyage dans
// `pagination.Filter` (même convention que tous les *GetPaged* du projet, pas de paramètre
// séparé). Domaine-agnostique : ne retourne que `ISelectOption<string>` (Id/Label), jamais un
// modèle métier (IAdminChoirListItem.Name, IAdminEventListItem.Title, etc.) — c'est ce qui
// permet à cette modale de servir identiquement pour chorales ET événements sans duplication.
export type EntitySearchFn = (pagination: IPaginationQueryParams) => Observable<IPaginatedResult<ISelectOption<string>>>;

// Modale générique de sélection multiple par recherche paginée (remplace les filtres
// "Identifiant de chorale"/"Identifiant d'événement" en UUID brut de user-list.component.ts —
// sélection par nom, recherche debouncée 300 ms déjà portée par DataTableComponent). La
// sélection est conservée dans une Map (Id -> Label) totalement indépendante de la page
// affichée : changer de page ou de recherche ne fait jamais perdre une coche déjà posée.
@Component({
  selector: 'app-entity-multi-select-modal',
  standalone: true,
  imports: [ModalComponent, DataTableComponent],
  templateUrl: './entity-multi-select-modal.component.html',
  styleUrl: './entity-multi-select-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EntityMultiSelectModalComponent {
  private readonly destroyRef = inject(DestroyRef);

  readonly title = input.required<string>();
  readonly searchFn = input.required<EntitySearchFn>();
  // Présélection à l'ouverture : reprend les chips déjà choisies si la modale est rouverte.
  readonly initialSelection = input<readonly ISelectOption<string>[]>([]);
  readonly emptyMessage = input<string>('Aucun résultat.');

  readonly confirmed = output<ISelectOption<string>[]>();
  readonly cancelled = output();

  private readonly tplCheckbox = viewChild<TemplateRef<{ $implicit: ISelectOption<string> }>>('tplCheckbox');

  protected readonly columns = computed<IDataTableColumn<ISelectOption<string>>[]>(() => [
    { key: 'selected', label: '', cellTemplate: this.tplCheckbox() },
    { key: 'Label', label: 'Nom' }
  ]);

  protected readonly items = signal<ISelectOption<string>[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  protected readonly filterText = signal('');

  // Id -> Label, jamais dérivée de `items()` : c'est précisément ce qui fait persister la
  // sélection à travers un changement de page ou de recherche (exigence explicite du plan).
  private readonly selected = signal<Map<string, string>>(new Map());

  protected readonly selectedCount = computed(() => this.selected().size);

  constructor() {
    // `searchFn` (input.required) et `initialSelection` ne sont peuplés qu'APRÈS la construction
    // du composant : les lire directement ici levait NG0950 ("Input is required but no value
    // was set"). Un effect() reporte cette lecture après le binding des inputs — même pattern
    // que ScoreViewerComponent. La modale est recréée à chaque ouverture (@if côté appelant,
    // jamais réutilisée), donc une lecture unique de la présélection reste correcte ; load() et
    // selected.set() sont volontairement `untracked` pour ne pas transformer cet effect en
    // rechargement réactif qui ferait doublon avec les appels explicites de
    // onFilterChange()/onPageChange() (page()/filterText() sont aussi lus par load()).
    effect(() => {
      const preselection = this.initialSelection();
      untracked(() => {
        this.selected.set(new Map(preselection.map(option => [option.Value, option.Label] as const)));
        this.load();
      });
    });
  }

  protected isSelected(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(option: ISelectOption<string>): void {
    this.selected.update(current => {
      const next = new Map(current);
      if (next.has(option.Value)) {
        next.delete(option.Value);
      } else {
        next.set(option.Value, option.Label);
      }
      return next;
    });
  }

  protected onFilterChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.load();
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected confirm(): void {
    this.confirmed.emit(Array.from(this.selected(), ([Value, Label]) => ({ Value, Label })));
  }

  protected cancel(): void {
    this.cancelled.emit();
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.searchFn()({
      Page: this.page(),
      PageSize: this.pageSize(),
      Filter: this.filterText() || undefined
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.items.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger la liste. Merci de réessayer.');
        }
      });
  }
}
