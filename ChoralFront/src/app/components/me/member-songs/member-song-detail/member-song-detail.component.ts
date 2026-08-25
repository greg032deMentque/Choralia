import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { ScoreService } from '@app/services/scores/score.service';
import { RecordingService } from '@app/services/recordings/recording.service';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { triggerBlobDownload } from '@core/file-download.util';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { AudioPlayerComponent } from '@app/components/shared/audio-player/audio-player.component';
import { ScoreViewerComponent } from '@app/components/scores/score-viewer/score-viewer.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISong } from '@models/songs-models/song.model';
import { IScore } from '@models/scores-models/score.model';
import { IRecording } from '@models/recordings-models/recording.model';
import { IAudioTrack } from '@models/recordings-models/audio-track.model';
import { ScoreStatusEnum } from '@app/enums/status-score.enum';
import { RecordingStatusEnum } from '@app/enums/status-recording.enum';
import { getTypeScoreLabel } from '@app/enums/type-score.enum';
import { RecordingTypeEnum, getTypeRecordingLabel } from '@app/enums/type-recording.enum';
import { ALL_VOICE_PARTS, VoicePartEnum, getVoicePartLabel, getVoicePartsLabel } from '@app/enums/voice-part.enum';

// Les deux blocs de la fiche paginent côté serveur (même convention que les « Listes de
// chants » de EventDetailComponent) : un chant peut accumuler versions et prises de voix.
const SCORES_PAGE_SIZE = 10;
const RECORDINGS_PAGE_SIZE = 10;

// Fiche chant du choriste : la partition publiée se consulte, l'enregistrement publié
// s'écoute. Aucune gestion — c'est la fin du parcours /me, pas une variante de
// SongDetailComponent (/management), qui édite, archive et gère les consignes.
//
// Aucun endpoint nouveau : GetById, GetPagedBySong (partitions) et GetPagedBySong
// (enregistrements) ne portent que [Authorize] — la restriction au périmètre du membre est
// faite côté service back, pas ici.
//
// La voix du membre n'est PAS présupposée : aucune route accessible à un choriste ne l'expose
// aujourd'hui. Le filtre de voix est donc un choix explicite, et « Toutes » par défaut —
// cohérent avec l'ouverture inter-voix en lecture côté back.
@Component({
  selector: 'app-member-song-detail',
  standalone: true,
  imports: [RouterLink, PaginationComponent, DataStateComponent, AudioPlayerComponent, ScoreViewerComponent, IconComponent],
  templateUrl: './member-song-detail.component.html',
  styleUrl: './member-song-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MemberSongDetailComponent {
  private readonly songService = inject(SongService);
  private readonly scoreService = inject(ScoreService);
  private readonly recordingService = inject(RecordingService);
  private readonly destroyRef = inject(DestroyRef);

  /** Liée via withComponentInputBinding() — jamais de paramMap.get() nu (OWASP A01). */
  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly allVoicePart = ALL_VOICE_PARTS;
  protected readonly getTypeScoreLabel = getTypeScoreLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getVoicePartsLabel = getVoicePartsLabel;

  readonly song = signal<ISong | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly scores = signal<IScore[]>([]);
  readonly scoresTotalCount = signal(0);
  readonly scoresLoading = signal(false);
  readonly scoresError = signal<string | null>(null);
  readonly scoresPage = signal(1);

  readonly recordings = signal<IRecording[]>([]);
  readonly recordingsTotalCount = signal(0);
  readonly recordingsLoading = signal(false);
  readonly recordingsError = signal<string | null>(null);
  readonly recordingsPage = signal(1);
  readonly voicePartFilter = signal<VoicePartEnum | null>(null);

  readonly viewerScoreId = signal<string | null>(null);

  readonly scoresTotalPages = computed(() => Math.max(1, Math.ceil(this.scoresTotalCount() / SCORES_PAGE_SIZE)));
  readonly recordingsTotalPages = computed(() =>
    Math.max(1, Math.ceil(this.recordingsTotalCount() / RECORDINGS_PAGE_SIZE))
  );

  // Projection enregistrement -> piste du lecteur. Ici toutes les pistes portent le même chant :
  // ce qui les distingue est la voix, elle tient donc le titre, et le propriétaire du contenu
  // le sous-titre.
  readonly tracks = computed<IAudioTrack[]>(() =>
    this.recordings()
      .filter((recording): recording is IRecording & { Id: string } => recording.Id !== null)
      .map(recording => ({
        RecordingId: recording.Id,
        Title:
          recording.Type === RecordingTypeEnum.ByVoicePart && recording.TargetVoicePart !== null
            ? getVoicePartLabel(recording.TargetVoicePart)
            : getTypeRecordingLabel(recording.Type),
        Subtitle: recording.ContentOwner,
        DurationSeconds: recording.DurationSeconds
      }))
  );

  // Le lecteur ne joue que la page affichée : sans cette précision, un membre pourrait croire
  // que la playlist couvre tout le chant alors qu'elle s'arrête à la page courante.
  readonly playerEmptyMessage = computed(() => {
    const voicePart = this.voicePartFilter();
    return voicePart === null
      ? 'Aucun enregistrement publié pour ce chant.'
      : `Aucun enregistrement publié pour la voix ${getVoicePartLabel(voicePart)}.`;
  });

  constructor() {
    // `id` est un signal input : non peuplé à la construction. load() gère lui-même l'id
    // absent ou invalide — un garde ici laisserait l'écran en chargement infini.
    effect(() => {
      this.load();
    });
  }

  onVoicePartFilterChange(value: string): void {
    this.voicePartFilter.set(value === '' ? null : (Number(value) as VoicePartEnum));
    this.recordingsPage.set(1);
    this.loadRecordings();
  }

  goToScoresPage(page: number): void {
    if (page < 1 || page > this.scoresTotalPages()) return;
    this.scoresPage.set(page);
    this.loadScores();
  }

  goToRecordingsPage(page: number): void {
    if (page < 1 || page > this.recordingsTotalPages()) return;
    this.recordingsPage.set(page);
    this.loadRecordings();
  }

  openViewer(score: IScore): void {
    if (!score.Id) return;
    this.viewerScoreId.set(score.Id);
  }

  closeViewer(): void {
    this.viewerScoreId.set(null);
  }

  // `10-D5` : le téléchargement est un droit porté par le contenu, distinct de la consultation.
  downloadScore(score: IScore): void {
    if (!score.Id || !score.DownloadAllowed) return;

    this.scoreService
      .download(score.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => triggerBlobDownload(blob, score.OriginalFileName ?? `partition-${score.Version}`),
        error: () => this.scoresError.set('Impossible de télécharger cette partition.')
      });
  }

  downloadRecording(recording: IRecording): void {
    if (!recording.Id || !recording.DownloadAllowed) return;

    this.recordingService
      .download(recording.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => triggerBlobDownload(blob, recording.OriginalFileName ?? `enregistrement-${recording.Id}`),
        error: () => this.recordingsError.set('Impossible de télécharger cet enregistrement.')
      });
  }

  protected load(): void {
    const songId = this.id();
    if (!isValidUuid(songId)) {
      this.loading.set(false);
      this.error.set('Identifiant de chant invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.songService
      .getById(songId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: song => {
          this.song.set(song);
          this.loading.set(false);
          this.loadScores();
          this.loadRecordings();
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger ce chant.');
        }
      });
  }

  protected loadScores(): void {
    const songId = this.id();
    if (!isValidUuid(songId)) return;

    this.scoresLoading.set(true);
    this.scoresError.set(null);

    this.scoreService
      .getPagedBySong(
        songId,
        { Status: ScoreStatusEnum.Published },
        { Page: this.scoresPage(), PageSize: SCORES_PAGE_SIZE, SortActive: 'Version', SortDirection: 'asc' }
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.scores.set(result.Items);
          this.scoresTotalCount.set(result.TotalCount);
          this.scoresLoading.set(false);
        },
        error: () => {
          this.scoresLoading.set(false);
          this.scoresError.set('Impossible de charger les partitions de ce chant.');
        }
      });
  }

  protected loadRecordings(): void {
    const songId = this.id();
    if (!isValidUuid(songId)) return;

    this.recordingsLoading.set(true);
    this.recordingsError.set(null);

    this.recordingService
      .getPagedBySong(
        songId,
        { Status: RecordingStatusEnum.Published, TargetVoicePart: this.voicePartFilter() ?? undefined },
        { Page: this.recordingsPage(), PageSize: RECORDINGS_PAGE_SIZE, SortActive: 'CreatedAt', SortDirection: 'desc' }
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.recordings.set(result.Items);
          this.recordingsTotalCount.set(result.TotalCount);
          this.recordingsLoading.set(false);
        },
        error: () => {
          this.recordingsLoading.set(false);
          this.recordingsError.set('Impossible de charger les enregistrements de ce chant.');
        }
      });
  }
}
