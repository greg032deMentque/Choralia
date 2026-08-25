import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongListService } from '@app/services/song-lists/song-list.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { SongListFormComponent } from '@app/components/song-lists/song-list-form/song-list-form.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISongList } from '@models/song-lists-models/song-list.model';
import { getTypeListLabel } from '@app/enums/type-list.enum';
import { SongListStatusEnum, getStatusListLabel } from '@app/enums/status-list.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Liste paginée de toutes les lists de chants (SongList). SongListPagedFilterViewModel
// (back) n'expose pas de filtre ChoirId (contrairement à SongController/
// EventController) — connu et documenté, hors périmètre de correction pour ce lot
// front-only (contrat back existant). Composition/édition/suppression : Responsable +
// SectionLeader (pas de restriction Type ici — la nuance SectionLeader/Type=Pupitre ne
// s'applique qu'aux actions de workflow, gérées dans SongListDetailComponent).
@Component({
  selector: 'app-song-list-list',
  standalone: true,
  imports: [RouterLink, DataTableComponent, PageHeaderComponent, SongListFormComponent, IconComponent],
  templateUrl: './song-list-list.component.html',
  styleUrl: './song-list-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongListListComponent {
  private readonly songListService = inject(SongListService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly RoutePaths = RoutePaths;
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getTypeListLabel = getTypeListLabel;
  protected readonly getStatusListLabel = getStatusListLabel;
  protected readonly SongListStatusEnum = SongListStatusEnum;

  private readonly tplName = viewChild<TemplateRef<{ $implicit: ISongList }>>('tplName');
  private readonly tplType = viewChild<TemplateRef<{ $implicit: ISongList }>>('tplType');
  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: ISongList }>>('tplStatus');
  private readonly tplSongCount = viewChild<TemplateRef<{ $implicit: ISongList }>>('tplSongCount');
  private readonly tplActions = viewChild<TemplateRef<{ $implicit: ISongList }>>('tplActions');

  readonly items = signal<ISongList[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filterText = signal('');

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly columns = computed<IDataTableColumn<ISongList>[]>(() => [
    { key: 'Name', label: 'Nom', sortable: true, cellTemplate: this.tplName() },
    { key: 'Type', label: 'Type', cellTemplate: this.tplType() },
    { key: 'Status', label: 'Statut', cellTemplate: this.tplStatus() },
    { key: 'Songs', label: 'Chants', cellTemplate: this.tplSongCount() },
    { key: 'Actions', label: 'Actions', cellTemplate: this.tplActions() }
  ]);

  readonly showForm = signal(false);
  readonly editingSongList = signal<ISongList | null>(null);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  constructor() {
    this.load();
  }

  // L'anti-rebond de la recherche est porté par DataTableComponent.
  onFilterTextChange(value: string): void {
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

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  openSongList(songList: ISongList): void {
    if (!songList.Id) return;
    this.router.navigate(['/', RoutePaths.Management, this.spaceId(), RoutePaths.SongLists, songList.Id]);
  }

  openCreateForm(): void {
    this.editingSongList.set(null);
    this.showForm.set(true);
  }

  openEditForm(songList: ISongList): void {
    this.editingSongList.set(songList);
    this.showForm.set(true);
  }

  onFormSaved(): void {
    this.showForm.set(false);
    this.editingSongList.set(null);
    this.load();
  }

  onFormCancelled(): void {
    this.showForm.set(false);
    this.editingSongList.set(null);
  }

  async deleteSongList(songList: ISongList): Promise<void> {
    if (!songList.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cette liste',
      message: `« ${songList.Name} » sera supprimée définitivement.`,
      impacts: ['Les chants qui la composent ne sont pas supprimés du répertoire.'],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.songListService
      .delete(songList.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${songList.Name} » a été supprimée.`);
          this.load();
        },
        error: () => this.toast.error('Impossible de supprimer cette liste. Merci de réessayer.')
      });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    this.songListService
      .getPaged({
        Page: this.page(),
        PageSize: this.pageSize(),
        SortActive: this.sortActive(),
        SortDirection: this.sortDirection(),
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
          this.error.set('Impossible de charger les listes de chants. Merci de réessayer.');
        }
      });
  }
}
