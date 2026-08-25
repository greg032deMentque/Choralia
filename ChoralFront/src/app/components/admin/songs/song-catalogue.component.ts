import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminSongService } from '@app/services/admin/admin-song.service';
import { parseBooleanQueryParam } from '@core/query-params.util';
import { DataTableComponent, DEFAULT_PAGE_SIZE, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminSongCatalogItem } from '@models/admin-models/admin-song-catalog-item.model';
import { IAdminSongGroupChoirItem } from '@models/admin-models/admin-song-group-choir-item.model';
import { getSongStatusLabel } from '@app/enums/song-status.enum';


// Seuil d'affichage (pas de valeur imposée par la spec — décision assumée) : un groupe porté
// par au moins ce nombre de chorales est mis en évidence, c'est l'information que cet écran
// existe pour donner. Une seule chorale n'est jamais un doublon (cf. ChoirCount === 1),
// donc ce seuil reste toujours strictement supérieur à 1.
const LARGE_HEADCOUNT_THRESHOLD = 5;

// Catalogue transverse des chants pour l'administration générale (lot 4) — regroupement
// d'AFFICHAGE uniquement (décision utilisateur) : aucune entité Oeuvre, aucune fusion,
// aucune écriture. L'admin voit le catalogue, il n'entre pas dans le contenu (aucune action
// d'écriture, aucun lien de téléchargement sur cet écran).
//
// DataTableComponent (figé) ne permet pas d'insérer une ligne de détail dépliable dans la
// table elle-même (pas de <tr> additionnel piloté par colonne). Choix retenu : un panneau de
// détail sous le tableau, piloté par rowClick — au clic sur une ligne, on bascule l'état
// "groupe déplié" (même clic sur la ligne déjà dépliée = repli) et on charge
// GetChoralesDuGroupe la première fois seulement (mise en cache par `cle`, jamais de second
// appel au repli/redépliement). Ce panneau a son propre DataStateComponent en
// variant="spinner", indépendant du chargement du tableau principal.
@Component({
  selector: 'app-song-catalogue',
  standalone: true,
  imports: [DatePipe, DataTableComponent, DataStateComponent, PageHeaderComponent, IconComponent],
  templateUrl: './song-catalogue.component.html',
  styleUrl: './song-catalogue.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongCatalogueComponent {
  private readonly adminSongService = inject(AdminSongService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getSongStatusLabel = getSongStatusLabel;
  protected readonly LARGE_HEADCOUNT_THRESHOLD = LARGE_HEADCOUNT_THRESHOLD;

  private readonly tplExpand = viewChild<TemplateRef<{ $implicit: IAdminSongCatalogItem }>>('tplExpand');
  private readonly tplComposer = viewChild<TemplateRef<{ $implicit: IAdminSongCatalogItem }>>('tplCompositeur');
  private readonly tplChoirCount = viewChild<TemplateRef<{ $implicit: IAdminSongCatalogItem }>>('tplNombreChorales');

  // Seules Titre, Composer et ChoirCount sont réellement triées côté serveur (liste
  // blanche du contrat) : OccurrenceCount et Cle ne sont pas déclarées sortable.
  readonly columns = computed<IDataTableColumn<IAdminSongCatalogItem>[]>(() => [
    { key: 'Expand', label: '', cellTemplate: this.tplExpand() },
    { key: 'Title', label: 'Titre', sortable: true },
    { key: 'Composer', label: 'Compositeur', sortable: true, cellTemplate: this.tplComposer() },
    { key: 'ChoirCount', label: 'Count de chorales', sortable: true, cellTemplate: this.tplChoirCount() },
    { key: 'OccurrenceCount', label: "Count d'occurrences" }
  ]);

  readonly items = signal<IAdminSongCatalogItem[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly filterText = signal('');
  // Valeur initiale lue depuis le query param de la navigation (ex. tuile "Groupes en doublon"
  // du tableau de bord admin, voir dashboard.component.ts) — ActivatedRouteSnapshot, lecture
  // unique au chargement. Absent ou malformé (ex. "peut-être") → false silencieusement (voir
  // query-params.util.ts), jamais d'exception. Ensuite ce signal reste un filtre normal,
  // modifiable par la case à cocher (onDuplicatesOnlyChange).
  readonly doublonsUniquement = signal(parseBooleanQueryParam(this.route.snapshot.queryParamMap, 'DuplicatesOnly') ?? false);

  // Panneau de détail (dépliage) — indépendant du chargement du tableau principal.
  readonly expandedKey = signal<string | null>(null);
  readonly expandedTitle = signal<string | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal<string | null>(null);
  private readonly detailsCache = signal<ReadonlyMap<string, IAdminSongGroupChoirItem[]>>(new Map());

  readonly expandedItems = computed<IAdminSongGroupChoirItem[]>(() => {
    const key = this.expandedKey();
    if (key === null) return [];
    return this.detailsCache().get(key) ?? [];
  });

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

  onDuplicatesOnlyChange(value: boolean): void {
    this.doublonsUniquement.set(value);
    // Repartir en page 1 : rester en page 4 d'un résultat filtré qui n'en compte qu'une seule
    // afficherait une liste vide et incompréhensible.
    this.page.set(1);
    this.load();
  }

  onRowClick(row: IAdminSongCatalogItem): void {
    if (this.expandedKey() === row.Key) {
      this.closeDetail();
      return;
    }

    this.expandedKey.set(row.Key);
    this.expandedTitle.set(row.Title);
    this.detailError.set(null);

    // Déjà chargé (dépliage précédent de ce même groupe) : pas de nouvel appel.
    if (this.detailsCache().has(row.Key)) return;

    this.loadDetail(row.Key);
  }

  closeDetail(): void {
    this.expandedKey.set(null);
    this.expandedTitle.set(null);
    this.detailError.set(null);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.adminSongService
      .getPagedCatalogue(
        {
          Page: this.page(),
          PageSize: this.pageSize(),
          SortActive: this.sortActive(),
          SortDirection: this.sortDirection(),
          Filter: this.filterText() || undefined
        },
        { DuplicatesOnly: this.doublonsUniquement() ? true : undefined }
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
          this.error.set('Impossible de charger le catalogue des chants. Merci de réessayer.');
        }
      });
  }

  private loadDetail(key: string): void {
    this.detailLoading.set(true);

    this.adminSongService
      .getChoirsDuGroup(key)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: choirsDuGroup => {
          const next = new Map(this.detailsCache());
          next.set(key, choirsDuGroup);
          this.detailsCache.set(next);
          this.detailLoading.set(false);
        },
        error: () => {
          this.detailLoading.set(false);
          this.detailError.set('Impossible de charger le détail de ce groupe. Merci de réessayer.');
        }
      });
  }
}
