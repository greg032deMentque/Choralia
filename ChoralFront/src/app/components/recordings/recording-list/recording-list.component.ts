import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RecordingService } from '@app/services/recordings/recording.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { debounce } from '@core/debounce.util';
import { SongPickerComponent } from '@app/components/shared/song-picker/song-picker.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { RecordingUploadFormComponent } from '@app/components/recordings/recording-upload-form/recording-upload-form.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { triggerBlobDownload } from '@core/file-download.util';
import { IRecording } from '@models/recordings-models/recording.model';
import { RecordingTypeEnum, getTypeRecordingLabel } from '@app/enums/type-recording.enum';
import { RecordingStatusEnum, getStatusRecordingLabel } from '@app/enums/status-recording.enum';
import { RecordingSourceEnum, getSourceRecordingLabel } from '@app/enums/source-recording.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

const DEFAULT_PAGE_SIZE = 10;
const FILTER_DEBOUNCE_MS = 300;

// Liste des enregistrements d'un chant — GetPagedBySong exige SongId (même logique que
// ScoreListComponent). Publish/Reject réservés au rôle Responsable (pas SectionLeader,
// aucune délégation dans ce lot — décision documentée bloc de transfert).
@Component({
  selector: 'app-recording-list',
  standalone: true,
  imports: [PaginationComponent, DatePipe, DataStateComponent, SongPickerComponent, PageHeaderComponent, RecordingUploadFormComponent, IconComponent],
  templateUrl: './recording-list.component.html',
  styleUrl: './recording-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class RecordingListComponent {
  private readonly recordingService = inject(RecordingService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly RecordingStatusEnum = RecordingStatusEnum;
  protected readonly getTypeRecordingLabel = getTypeRecordingLabel;
  protected readonly getStatusRecordingLabel = getStatusRecordingLabel;
  protected readonly getSourceRecordingLabel = getSourceRecordingLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allTypes: RecordingTypeEnum[] = [RecordingTypeEnum.General, RecordingTypeEnum.ByVoicePart];
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];
  protected readonly allStatuss: RecordingStatusEnum[] = [
    RecordingStatusEnum.Draft,
    RecordingStatusEnum.PendingReview,
    RecordingStatusEnum.Published,
    RecordingStatusEnum.Archived
  ];
  protected readonly allSources: RecordingSourceEnum[] = [
    RecordingSourceEnum.RecordedInApp,
    RecordingSourceEnum.UploadedFile,
    RecordingSourceEnum.Shared
  ];

  readonly selectedSongId = signal<string | null>(null);
  // Alimenté par SongPickerComponent : sert uniquement à taire les messages de CETTE liste
  // quand il n'y a aucun chant à choisir — le sélecteur porte déjà l'explication et la sortie.
  readonly repertoireEmpty = signal(false);

  readonly items = signal<IRecording[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly typeFilter = signal<RecordingTypeEnum | null>(null);
  readonly voicePartFilter = signal<VoicePartEnum | null>(null);
  readonly statusFilter = signal<RecordingStatusEnum | null>(null);
  readonly sourceFilter = signal<RecordingSourceEnum | null>(null);
  readonly filterText = signal('');

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  readonly showUploadForm = signal(false);
  readonly editingId = signal<string | null>(null);
  readonly editContentOwner = signal('');
  readonly editDownloadAllowed = signal(true);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  // Publish/Reject réservés au Responsable — pas de délégation SectionLeader dans ce lot.
  protected readonly canPublish = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    return this.authStore.activeSpaceRoles().includes(UserRoleEnum.Manager);
  });

  // Anti-rebond sur la saisie du filtre texte (300 ms) — évite un appel HTTP par frappe.
  private readonly debouncedLoad = debounce(() => this.load(), FILTER_DEBOUNCE_MS);

  onSongChange(songId: string | null): void {
    this.selectedSongId.set(songId);
    this.page.set(1);
    this.load();
  }

  onTypeFilterChange(value: string): void {
    this.typeFilter.set(value === '' ? null : (Number(value) as RecordingTypeEnum));
    this.page.set(1);
    this.load();
  }

  onVoicePartFilterChange(value: string): void {
    this.voicePartFilter.set(value === '' ? null : (Number(value) as VoicePartEnum));
    this.page.set(1);
    this.load();
  }

  onStatusFilterChange(value: string): void {
    this.statusFilter.set(value === '' ? null : (Number(value) as RecordingStatusEnum));
    this.page.set(1);
    this.load();
  }

  onSourceFilterChange(value: string): void {
    this.sourceFilter.set(value === '' ? null : (Number(value) as RecordingSourceEnum));
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

  startEdit(recording: IRecording): void {
    if (!recording.Id) return;
    this.editingId.set(recording.Id);
    this.editContentOwner.set(recording.ContentOwner);
    this.editDownloadAllowed.set(recording.DownloadAllowed);
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  saveEdit(recording: IRecording): void {
    if (!recording.Id) return;
    this.error.set(null);
    this.recordingService
      .update(recording.Id, {
        ContentOwner: this.editContentOwner(),
        DownloadAllowed: this.editDownloadAllowed()
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.editingId.set(null);
          this.load();
        },
        error: () => this.error.set('Impossible de mettre à jour cet enregistrement.')
      });
  }

  sendAValidation(recording: IRecording): void {
    if (!recording.Id) return;
    this.error.set(null);
    this.recordingService
      .sendAValidation(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.load(),
        error: () => this.error.set('Impossible d\'envoyer cet enregistrement à validation.')
      });
  }

  publish(recording: IRecording): void {
    if (!recording.Id) return;
    this.error.set(null);
    this.recordingService
      .publish(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.load(),
        error: () => this.error.set('Impossible de publier cet enregistrement.')
      });
  }

  // Rejet : la contribution repart en brouillon chez son auteur, qui en est informé —
  // conséquence visible par un tiers, donc confirmation explicite (Spec §6.4).
  async reject(recording: IRecording): Promise<void> {
    if (!recording.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Rejeter cet enregistrement',
      message: `L'enregistrement de « ${recording.ContentOwner} » ne sera pas publié.`,
      impacts: ['Son auteur pourra le corriger et le soumettre à nouveau.'],
      confirmationLabel: 'Rejeter',
      danger: true
    });
    if (!confirmed) return;

    this.recordingService
      .reject(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Enregistrement rejeté.');
          this.load();
        },
        error: () => this.toast.error('Impossible de rejeter cet enregistrement.')
      });
  }

  // Archivage réversible (`restore` existe côté API) : pas de modale, action immédiate et
  // annulation portée par le toast (Spec §6.4, décision `10-D42`).
  archive(recording: IRecording): void {
    if (!recording.Id) return;
    const recordingId = recording.Id;
    this.error.set(null);

    this.recordingService
      .archive(recordingId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.load();
          this.toast.undoable('Enregistrement archivé', 'Annuler', () => this.restoreById(recordingId));
        },
        error: () => this.toast.error("Impossible d'archiver cet enregistrement.")
      });
  }

  restore(recording: IRecording): void {
    if (!recording.Id) return;
    this.restoreById(recording.Id);
  }

  private restoreById(recordingId: string): void {
    this.error.set(null);
    this.recordingService
      .restore(recordingId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.load(),
        error: () => this.toast.error('Impossible de restaurer cet enregistrement.')
      });
  }

  // Suppression définitive, sans inverse : modale de confirmation (Spec §6.4).
  async deleteRecording(recording: IRecording): Promise<void> {
    if (!recording.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cet enregistrement',
      message: `L'enregistrement de « ${recording.ContentOwner} » et son fichier audio seront supprimés définitivement.`,
      impacts: ['Pour le retirer sans perdre le fichier, archivez-le à la place.'],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.recordingService
      .delete(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Enregistrement supprimé.');
          this.load();
        },
        error: () => this.toast.error('Impossible de supprimer cet enregistrement.')
      });
  }

  // `10-D5` : même règle que pour les partitions — le droit de téléchargement est porté par le
  // contenu. Le gabarit masque le bouton, ce garde ferme l'appel direct.
  download(recording: IRecording): void {
    if (!recording.Id || !recording.DownloadAllowed) return;

    this.recordingService
      .download(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => triggerBlobDownload(blob, recording.OriginalFileName ?? `enregistrement-${recording.Id}`),
        error: () => this.error.set('Impossible de télécharger cet enregistrement.')
      });
  }

  private load(): void {
    const songId = this.selectedSongId();
    if (!songId) return;

    this.loading.set(true);
    this.error.set(null);

    this.recordingService
      .getPagedBySong(
        songId,
        {
          Type: this.typeFilter() ?? undefined,
          TargetVoicePart: this.voicePartFilter() ?? undefined,
          Status: this.statusFilter() ?? undefined,
          Source: this.sourceFilter() ?? undefined
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
          this.error.set('Impossible de charger les enregistrements. Merci de réessayer.');
        }
      });
  }
}
