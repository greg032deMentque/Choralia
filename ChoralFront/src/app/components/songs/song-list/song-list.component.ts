import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  IDataTableChip,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { SongFormComponent } from '@app/components/songs/song-form/song-form.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISong } from '@models/songs-models/song.model';
import { SongStatusEnum, getSongStatusLabel } from '@app/enums/song-status.enum';
import { SongPriorityEnum, getPrioritySongLabel } from '@app/enums/priority-song.enum';
import { VoicePartEnum, getVoicePartLabel, getVoicePartsLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Liste paginée des chants du répertoire de la chorale active (ChoirId toujours transmis
// explicitement — SongController.GetPaged ne scope pas automatiquement via X-Space-Id,
// pattern déjà appliqué par EventService/SongListService).
//
// Tri, pagination, recherche, filtres repliables et rendu carte sous 768 px sont délégués à
// DataTableComponent : cet écran ne réimplémente plus son propre tableau.
@Component({
  selector: 'app-song-list',
  standalone: true,
  imports: [RouterLink, DataTableComponent, PageHeaderComponent, SongFormComponent, IconComponent],
  templateUrl: './song-list.component.html',
  styleUrl: './song-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongListComponent {
  private readonly songService = inject(SongService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly RoutePaths = RoutePaths;
  // Espace actif : garanti non-null en consommation réelle (route protégée par spaceRoleGuard,
  // qui synchronise l'espace actif avant d'activer la route) — fallback '' pour ne jamais
  // casser un routerLink en cas de rendu hors contexte de route (tests unitaires).
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly SongStatusEnum = SongStatusEnum;
  protected readonly getSongStatusLabel = getSongStatusLabel;
  protected readonly getPrioritySongLabel = getPrioritySongLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getVoicePartsLabel = getVoicePartsLabel;
  protected readonly allStatuss: SongStatusEnum[] = [SongStatusEnum.Active, SongStatusEnum.Archived];
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];
  protected readonly allPrioritys: SongPriorityEnum[] = [SongPriorityEnum.Low, SongPriorityEnum.Normal, SongPriorityEnum.High];

  private readonly tplTitle = viewChild<TemplateRef<{ $implicit: ISong }>>('tplTitle');
  private readonly tplVoiceParts = viewChild<TemplateRef<{ $implicit: ISong }>>('tplVoiceParts');
  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: ISong }>>('tplStatus');
  private readonly tplPriority = viewChild<TemplateRef<{ $implicit: ISong }>>('tplPriority');
  private readonly tplCompleteness = viewChild<TemplateRef<{ $implicit: ISong }>>('tplCompleteness');
  private readonly tplActions = viewChild<TemplateRef<{ $implicit: ISong }>>('tplActions');

  readonly items = signal<ISong[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filterText = signal('');
  readonly statusFilter = signal<SongStatusEnum | null>(null);
  readonly voicePartFilter = signal<VoicePartEnum | null>(null);
  readonly priorityFilter = signal<SongPriorityEnum | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly showForm = signal(false);
  readonly editingSong = signal<ISong | null>(null);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  // computed() plutôt qu'une valeur figée au constructeur : les viewChild(TemplateRef) ne sont
  // résolus qu'après la première passe de vue.
  readonly columns = computed<IDataTableColumn<ISong>[]>(() => [
    { key: 'Title', label: 'Titre', sortable: true, cellTemplate: this.tplTitle() },
    { key: 'VoiceParts', label: 'Voix concernées', cellTemplate: this.tplVoiceParts() },
    { key: 'Status', label: 'Statut', sortable: true, cellTemplate: this.tplStatus() },
    { key: 'Priority', label: 'Priorité', sortable: true, cellTemplate: this.tplPriority() },
    { key: 'IsCompleteForChoir', label: 'Complétude', cellTemplate: this.tplCompleteness() },
    { key: 'Actions', label: 'Actions', cellTemplate: this.tplActions() }
  ]);

  readonly activeFilters = computed<IDataTableChip[]>(() => {
    const chips: IDataTableChip[] = [];

    const status = this.statusFilter();
    if (status !== null) {
      chips.push({ key: 'Status', label: `Statut : ${getSongStatusLabel(status)}` });
    }

    const voicePart = this.voicePartFilter();
    if (voicePart !== null) {
      chips.push({ key: 'VoicePart', label: `Voix : ${getVoicePartLabel(voicePart)}` });
    }

    const priority = this.priorityFilter();
    if (priority !== null) {
      chips.push({ key: 'Priority', label: `Priorité : ${getPrioritySongLabel(priority)}` });
    }

    return chips;
  });

  constructor() {
    this.load();
  }

  // L'anti-rebond de la recherche est porté par DataTableComponent : le doubler ici
  // ajouterait 300 ms d'attente supplémentaire à chaque frappe.
  onFilterTextChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.load();
  }

  onStatusFilterChange(value: string): void {
    this.statusFilter.set(value === '' ? null : (Number(value) as SongStatusEnum));
    this.page.set(1);
    this.load();
  }

  onVoicePartFilterChange(value: string): void {
    this.voicePartFilter.set(value === '' ? null : (Number(value) as VoicePartEnum));
    this.page.set(1);
    this.load();
  }

  onPriorityFilterChange(value: string): void {
    this.priorityFilter.set(value === '' ? null : (Number(value) as SongPriorityEnum));
    this.page.set(1);
    this.load();
  }

  onFilterRemove(key: string): void {
    switch (key) {
      case 'Status':
        this.statusFilter.set(null);
        break;
      case 'VoicePart':
        this.voicePartFilter.set(null);
        break;
      case 'Priority':
        this.priorityFilter.set(null);
        break;
      default:
        return;
    }

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

  openSong(song: ISong): void {
    if (!song.Id) return;
    this.router.navigate(['/', RoutePaths.Management, this.spaceId(), RoutePaths.Songs, song.Id]);
  }

  openCreateForm(): void {
    this.editingSong.set(null);
    this.showForm.set(true);
  }

  openEditForm(song: ISong): void {
    this.editingSong.set(song);
    this.showForm.set(true);
  }

  onFormSaved(): void {
    this.showForm.set(false);
    this.editingSong.set(null);
    this.load();
  }

  onFormCancelled(): void {
    this.showForm.set(false);
    this.editingSong.set(null);
  }

  missingVoicePartLabel(song: ISong): string {
    return song.VoicePartsWithoutPublishedRecording.map(getVoicePartLabel).join(', ');
  }

  // Archivage : réversible côté métier mais sans endpoint de désarchivage exposé aujourd'hui —
  // donc modale de confirmation, et non annulation différée (voir 10-D42).
  async archiveSong(song: ISong): Promise<void> {
    if (!song.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Archiver ce chant',
      message: `« ${song.Title} » quittera le répertoire actif de la chorale.`,
      impacts: ['Le chant reste consultable via le filtre « Archivé ».'],
      confirmationLabel: 'Archiver',
      danger: true
    });
    if (!confirmed) return;

    this.songService
      .delete(song.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${song.Title} » a été archivé.`);
          this.load();
        },
        error: () => this.toast.error("Impossible d'archiver ce chant. Merci de réessayer.")
      });
  }

  private load(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) {
      this.error.set('Aucune chorale active sélectionnée.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.songService
      .getPaged(
        {
          ChoirId: choirId,
          VoicePart: this.voicePartFilter() ?? undefined,
          Status: this.statusFilter() ?? undefined,
          Priority: this.priorityFilter() ?? undefined
        },
        {
          Page: this.page(),
          PageSize: this.pageSize(),
          SortActive: this.sortActive(),
          SortDirection: this.sortDirection(),
          Filter: this.filterText() || undefined
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
          this.error.set('Impossible de charger les chants. Merci de réessayer.');
        }
      });
  }
}
