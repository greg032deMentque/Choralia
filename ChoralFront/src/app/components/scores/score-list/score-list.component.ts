import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ScoreService } from '@app/services/scores/score.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { debounce } from '@core/debounce.util';
import { SongPickerComponent } from '@app/components/shared/song-picker/song-picker.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { ScoreUploadFormComponent } from '@app/components/scores/score-upload-form/score-upload-form.component';
import { ScoreViewerComponent } from '@app/components/scores/score-viewer/score-viewer.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { triggerBlobDownload } from '@core/file-download.util';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IScore } from '@models/scores-models/score.model';
import { ScoreTypeEnum, getTypeScoreLabel } from '@app/enums/type-score.enum';
import { ScoreStatusEnum, getStatusScoreLabel } from '@app/enums/status-score.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

const DEFAULT_PAGE_SIZE = 10;
const FILTER_DEBOUNCE_MS = 300;

// Liste des partitions d'un chant — GetPagedBySong exige SongId (pas de vue toutes-
// chorales/tous-chants dans ce lot). Le sélecteur de chant ci-dessous en tient lieu de
// filtre principal, Type/TargetVoicePart/Status en filtres secondaires.
@Component({
  selector: 'app-score-list',
  standalone: true,
  imports: [PaginationComponent, DatePipe, DataStateComponent, SongPickerComponent, PageHeaderComponent, ScoreUploadFormComponent, ScoreViewerComponent, IconComponent],
  templateUrl: './score-list.component.html',
  styleUrl: './score-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScoreListComponent {
  private readonly scoreService = inject(ScoreService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly ScoreStatusEnum = ScoreStatusEnum;
  protected readonly getTypeScoreLabel = getTypeScoreLabel;
  protected readonly getStatusScoreLabel = getStatusScoreLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allTypes: ScoreTypeEnum[] = [ScoreTypeEnum.General, ScoreTypeEnum.ByVoicePart];
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];
  protected readonly allStatuss: ScoreStatusEnum[] = [
    ScoreStatusEnum.Draft,
    ScoreStatusEnum.Published,
    ScoreStatusEnum.Archived
  ];

  readonly selectedSongId = signal<string | null>(null);
  // Alimenté par SongPickerComponent : sert uniquement à taire les messages de CETTE liste
  // quand il n'y a aucun chant à choisir — le sélecteur porte déjà l'explication et la sortie.
  readonly repertoireEmpty = signal(false);

  readonly items = signal<IScore[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly typeFilter = signal<ScoreTypeEnum | null>(null);
  readonly voicePartFilter = signal<VoicePartEnum | null>(null);
  readonly statusFilter = signal<ScoreStatusEnum | null>(null);
  readonly filterText = signal('');

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  readonly showUploadForm = signal(false);
  // Partition ouverte dans la visionneuse (null = visionneuse fermée).
  readonly viewerScoreId = signal<string | null>(null);
  readonly editingId = signal<string | null>(null);
  readonly editVersion = signal('');
  readonly editDownloadAllowed = signal(true);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  // Candidates à la bascule de version dans la visionneuse : les partitions publiées de la
  // page, plus celle réellement ouverte. Sans cette dernière, un responsable qui consulte un
  // brouillon verrait la visionneuse vide — la bascule reste, elle, limitée aux publiées
  // (`06-ecrans-application-mobile` § Partition).
  protected readonly viewerScores = computed<IScore[]>(() => {
    const openedId = this.viewerScoreId();
    if (openedId === null) return [];
    return this.items().filter(score => score.Id === openedId || score.Status === ScoreStatusEnum.Published);
  });

  // Anti-rebond sur la saisie du filtre texte (300 ms) — évite un appel HTTP par frappe.
  private readonly debouncedLoad = debounce(() => this.load(), FILTER_DEBOUNCE_MS);

  openViewer(score: IScore): void {
    if (!score.Id) return;
    this.viewerScoreId.set(score.Id);
  }

  closeViewer(): void {
    this.viewerScoreId.set(null);
  }

  onSongChange(songId: string | null): void {
    this.selectedSongId.set(songId);
    this.page.set(1);
    this.load();
  }

  onTypeFilterChange(value: string): void {
    this.typeFilter.set(value === '' ? null : (Number(value) as ScoreTypeEnum));
    this.page.set(1);
    this.load();
  }

  onVoicePartFilterChange(value: string): void {
    this.voicePartFilter.set(value === '' ? null : (Number(value) as VoicePartEnum));
    this.page.set(1);
    this.load();
  }

  onStatusFilterChange(value: string): void {
    this.statusFilter.set(value === '' ? null : (Number(value) as ScoreStatusEnum));
    this.page.set(1);
    this.load();
  }

  onFilterTextChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.debouncedLoad();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    this.load();
  }

  onUploadCreated(): void {
    this.showUploadForm.set(false);
    this.load();
  }

  startEdit(score: IScore): void {
    if (!score.Id) return;
    this.editingId.set(score.Id);
    this.editVersion.set(score.Version);
    this.editDownloadAllowed.set(score.DownloadAllowed);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  saveEdit(score: IScore): void {
    if (!score.Id) return;
    this.error.set(null);
    this.scoreService
      .update(score.Id, { Version: this.editVersion(), DownloadAllowed: this.editDownloadAllowed() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.editingId.set(null);
          this.load();
        },
        error: () => this.error.set('Impossible de mettre à jour cette partition.')
      });
  }

  publish(score: IScore): void {
    if (!score.Id) return;
    this.error.set(null);

    this.scoreService
      .publish(score.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.load();
          this.toast.success('Partition publiée.');
        },
        error: (err: HttpErrorResponse) => {
          if (err.status === 409) {
            this.error.set(
              'Cette partition a été modifiée entre-temps (publication concurrente). Rechargez la liste avant de réessayer.'
            );
          } else {
            this.error.set('Impossible de publier cette partition.');
          }
        }
      });
  }

  // `10-D42` : l'archivage est réversible, il se confirme donc APRÈS coup par un toast portant
  // son annulation, jamais par une modale AVANT — même contrat que l'archivage d'enregistrement
  // (recording-list.component.ts).
  archive(score: IScore): void {
    if (!score.Id) return;
    const scoreId = score.Id;
    this.error.set(null);
    this.scoreService
      .archive(scoreId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.load();
          this.toast.undoable('Partition archivée', 'Annuler', () => this.restoreById(scoreId));
        },
        error: () => this.error.set('Impossible d\'archiver cette partition.')
      });
  }

  restore(score: IScore): void {
    if (!score.Id) return;
    this.restoreById(score.Id);
  }

  private restoreById(scoreId: string): void {
    this.error.set(null);
    this.scoreService
      .restore(scoreId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.load(),
        error: () => this.toast.error('Impossible de restaurer cette partition.')
      });
  }

  // Suppression définitive, sans inverse : modale de confirmation (Spec §6.4).
  async deleteScore(score: IScore): Promise<void> {
    if (!score.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cette partition',
      message: `La version « ${score.Version} » et son fichier seront supprimés définitivement.`,
      impacts: ["Pour la retirer sans perdre le fichier, archivez-la à la place."],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.scoreService
      .delete(score.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Partition supprimée.');
          this.load();
        },
        error: () => this.toast.error('Impossible de supprimer cette partition.')
      });
  }

  // `10-D5` : le téléchargement est un droit porté par le contenu. Le gabarit masque déjà le
  // bouton quand il n'est pas accordé ; ce garde ferme l'appel direct (le back reste la source
  // de vérité et rejette de son côté).
  download(score: IScore): void {
    if (!score.Id || !score.DownloadAllowed) return;

    this.scoreService
      .download(score.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => triggerBlobDownload(blob, score.OriginalFileName ?? `partition-${score.Version}`),
        error: () => this.error.set('Impossible de télécharger cette partition.')
      });
  }

  private load(): void {
    const songId = this.selectedSongId();
    if (!songId) return;

    this.loading.set(true);
    this.error.set(null);

    this.scoreService
      .getPagedBySong(
        songId,
        { Type: this.typeFilter() ?? undefined, TargetVoicePart: this.voicePartFilter() ?? undefined, Status: this.statusFilter() ?? undefined },
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
          this.error.set('Impossible de charger les partitions. Merci de réessayer.');
        }
      });
  }
}
