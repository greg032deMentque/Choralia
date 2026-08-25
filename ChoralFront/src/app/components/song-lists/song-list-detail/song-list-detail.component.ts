import { effect, ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongListService } from '@app/services/song-lists/song-list.service';
import { SongService } from '@app/services/songs/song.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { SongListFormComponent } from '@app/components/song-lists/song-list-form/song-list-form.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISongList } from '@models/song-lists-models/song-list.model';
import { ISelectOption } from '@models/common-models/select-option.model';
import { getTypeListLabel } from '@app/enums/type-list.enum';
import { SongListStatusEnum, getStatusListLabel } from '@app/enums/status-list.enum';
import { SongListTypeEnum } from '@app/enums/type-list.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Liée via withComponentInputBinding() (app.config.ts) — jamais de paramMap.get() nu.
// L'id de route est validé (regex UUID) avant tout appel HTTP (OWASP A01).
// Composition (ajouter/retirer un chant) et édition des informations de base : Responsable
// + SectionLeader, sans restriction de Type. Workflow (Publish/Archive/RevertToDraft/
// ReorderSongs) : Responsable toujours, SectionLeader uniquement sur les lists
// Type=Pupitre (filtre UX — le back reste seul juge, cf. policy ChoirManagerOrSectionLeader
// et logique métier interne au service).
@Component({
  selector: 'app-song-list-detail',
  standalone: true,
  imports: [RouterLink, DataStateComponent, SongListFormComponent, IconComponent],
  templateUrl: './song-list-detail.component.html',
  styleUrl: './song-list-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongListDetailComponent {
  private readonly songListService = inject(SongListService);
  private readonly songService = inject(SongService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getTypeListLabel = getTypeListLabel;
  protected readonly getStatusListLabel = getStatusListLabel;
  protected readonly SongListStatusEnum = SongListStatusEnum;

  readonly songList = signal<ISongList | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly editing = signal(false);

  readonly songOptionsAll = signal<ISelectOption<string>[]>([]);
  readonly selectedSongIdToAdd = signal<string>('');
  readonly composingAction = signal(false);

  readonly sortedSongs = computed(() => [...(this.songList()?.Songs ?? [])].sort((a, b) => a.Position - b.Position));

  readonly availableSongOptions = computed(() => {
    const currentIds = new Set(this.sortedSongs().map(c => c.SongId));
    return this.songOptionsAll().filter(option => !currentIds.has(option.Value));
  });

  protected readonly canManageComposition = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  protected readonly canManageWorkflow = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    if (roles.includes(UserRoleEnum.Manager)) return true;
    return roles.includes(UserRoleEnum.SectionLeader) && this.songList()?.Type === SongListTypeEnum.Section;
  });

  readonly canReorder = computed(() => this.canManageWorkflow() && this.songList()?.Status === SongListStatusEnum.Draft);

  constructor() {
    // Voir song-detail : `id` est un signal input, non peuplé à la construction.
    // load() gère déjà l'id absent/invalide (isValidUuid) — un garde ici bloquerait ce cas
    // et laisserait l'écran en chargement infini.
    effect(() => {
      this.load();
    });

    // Indépendant de l'identifiant de la liste : chargé une fois. Le répertoire complet est
    // conservé tel quel — `availableSongOptions` en dérive ce qui reste ajoutable, un chant déjà
    // présent dans la liste ne devant pas être proposé deux fois.
    this.loadChoirRepertoire();
  }

  startEdit(): void {
    this.editing.set(true);
  }

  onFormSaved(updated: ISongList): void {
    this.songList.set(updated);
    this.editing.set(false);
  }

  onFormCancelled(): void {
    this.editing.set(false);
  }

  async deleteSongList(): Promise<void> {
    const current = this.songList();
    if (!current?.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cette liste',
      message: `« ${current.Name} » sera supprimée définitivement.`,
      impacts: ['Les chants qui la composent ne sont pas supprimés du répertoire.'],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.songListService
      .delete(current.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${current.Name} » a été supprimée.`);
          this.router.navigate(['/' + RoutePaths.Management, this.spaceId(), RoutePaths.SongLists]);
        },
        error: () => this.toast.error('Impossible de supprimer cette liste. Merci de réessayer.')
      });
  }

  onSelectedSongChange(value: string): void {
    this.selectedSongIdToAdd.set(value);
  }

  addSong(): void {
    const songListId = this.songList()?.Id;
    const songId = this.selectedSongIdToAdd();
    if (!songListId || !songId) return;

    this.composingAction.set(true);
    this.error.set(null);

    this.songListService
      .addSong(songListId, songId, this.sortedSongs().length)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.songList.set(updated);
          this.selectedSongIdToAdd.set('');
          this.composingAction.set(false);
        },
        error: () => {
          this.composingAction.set(false);
          this.error.set("Impossible d'ajouter ce chant à la liste.");
        }
      });
  }

  // Retrait réversible : `addSong` réinsère le chant à sa position d'origine. Une modale
  // pour un geste annulable en un clic serait de la friction pure (Spec §6.4, `10-D42`).
  removeSong(songId: string): void {
    const songListId = this.songList()?.Id;
    if (!songListId) return;

    const previousPosition = this.sortedSongs().findIndex(song => song.SongId === songId);

    this.composingAction.set(true);
    this.error.set(null);

    this.songListService
      .removeSong(songListId, songId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.composingAction.set(false);
          this.load();
          this.toast.undoable('Chant retiré de la liste', 'Annuler', () =>
            this.restoreSong(songListId, songId, previousPosition)
          );
        },
        error: () => {
          this.composingAction.set(false);
          this.error.set('Impossible de retirer ce chant de la liste.');
        }
      });
  }

  // Réinsertion à la position d'origine. `findIndex` renvoie -1 si le chant n'était pas
  // dans la liste chargée : on replace alors en fin plutôt que de refuser l'annulation.
  private restoreSong(songListId: string, songId: string, position: number): void {
    this.composingAction.set(true);

    this.songListService
      .addSong(songListId, songId, position >= 0 ? position : this.sortedSongs().length)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.songList.set(updated);
          this.composingAction.set(false);
        },
        error: () => {
          this.composingAction.set(false);
          this.toast.error("Impossible de remettre ce chant dans la liste.");
        }
      });
  }

  monter(index: number): void {
    if (index <= 0) return;
    this.swapAndReorder(index - 1, index);
  }

  descendre(index: number): void {
    if (index >= this.sortedSongs().length - 1) return;
    this.swapAndReorder(index, index + 1);
  }

  publish(): void {
    this.runWorkflowAction(id => this.songListService.publish(id), 'Impossible de publier cette liste.');
  }

  archive(): void {
    this.runWorkflowAction(id => this.songListService.archive(id), 'Impossible d\'archiver cette liste.');
  }

  repasserEnDraft(): void {
    this.runWorkflowAction(id => this.songListService.repasserEnDraft(id), 'Impossible de repasser cette liste en brouillon.');
  }

  private swapAndReorder(indexA: number, indexB: number): void {
    const songListId = this.songList()?.Id;
    if (!songListId) return;

    const songIds = this.sortedSongs().map(c => c.SongId);
    [songIds[indexA], songIds[indexB]] = [songIds[indexB], songIds[indexA]];

    this.composingAction.set(true);
    this.error.set(null);

    this.songListService
      .reorderSongs(songListId, songIds)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.songList.set(updated);
          this.composingAction.set(false);
        },
        error: () => {
          this.composingAction.set(false);
          this.error.set("Impossible de réordonner les chants de cette liste.");
        }
      });
  }

  private runWorkflowAction(action: (id: string) => ReturnType<SongListService['publish']>, errorMessage: string): void {
    const songListId = this.songList()?.Id;
    if (!songListId) return;

    this.composingAction.set(true);
    this.error.set(null);

    action(songListId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.songList.set(updated);
          this.composingAction.set(false);
        },
        error: () => {
          this.composingAction.set(false);
          this.error.set(errorMessage);
        }
      });
  }

  private load(): void {
    const songListId = this.id();
    if (!isValidUuid(songListId)) {
      this.loading.set(false);
      this.error.set('Identifiant de liste invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.songListService
      .getById(songListId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: songList => {
          this.songList.set(songList);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cette liste de chants.');
        }
      });
  }

  private loadChoirRepertoire(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) return;

    this.songService
      .getChoirOptions(choirId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: options => this.songOptionsAll.set(options),
        error: () => this.error.set('Impossible de charger la liste des chants disponibles.')
      });
  }
}
