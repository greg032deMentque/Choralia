import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ScoreService } from '@app/services/scores/score.service';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IScore } from '@models/scores-models/score.model';
import { getTypeScoreLabel } from '@app/enums/type-score.enum';
import { getVoicePartLabel } from '@app/enums/voice-part.enum';
import { triggerBlobDownload } from '@core/file-download.util';

/** Mode de rendu déduit de l'extension du fichier d'origine (whitelist de dépôt : pdf/png/jpg/jpeg). */
type ScoreRenderMode = 'pdf' | 'image' | 'unsupported';

const PDF_EXTENSIONS: readonly string[] = ['pdf'];
const IMAGE_EXTENSIONS: readonly string[] = ['png', 'jpg', 'jpeg'];

// Visionneuse de partition. Consultation À L'ÉCRAN, distincte du téléchargement : c'est cette
// distinction que porte `10-D5` (droit de téléchargement par contenu) et qu'un écran qui ne
// sait que forcer un download annule.
//
// Le fichier transite par ScoreService.download() (Blob via HttpClient) puis par un ObjectURL :
// jamais une URL d'API dans [src], que TokenInterceptor n'intercepterait pas — la requête
// partirait sans jeton.
@Component({
  selector: 'app-score-viewer',
  standalone: true,
  imports: [ModalComponent, IconComponent],
  templateUrl: './score-viewer.component.html',
  styleUrl: './score-viewer.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScoreViewerComponent {
  private readonly scoreService = inject(ScoreService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Partitions parmi lesquelles basculer — l'appelant transmet celles qu'il affiche. La
   * bascule proposée est restreinte aux versions du MÊME type et de la même voix cible
   * (`06-ecrans-application-mobile` § Partition), pas à toutes les partitions du chant.
   */
  readonly scores = input.required<IScore[]>();
  readonly initialScoreId = input.required<string>();

  readonly closed = output();

  protected readonly IconNameEnum = IconNameEnum;

  private readonly currentIdSignal = signal<string | null>(null);
  private readonly loadedScoreId = signal<string | null>(null);
  private readonly objectUrlSignal = signal<string | null>(null);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly current = computed<IScore | null>(() => {
    const id = this.currentIdSignal();
    if (id === null) return null;
    return this.scores().find(score => score.Id === id) ?? null;
  });

  readonly currentId = this.currentIdSignal.asReadonly();
  readonly objectUrl = this.objectUrlSignal.asReadonly();

  readonly title = computed(() => {
    const score = this.current();
    if (!score) return 'Partition';
    const voicePart = score.TargetVoicePart === null ? null : getVoicePartLabel(score.TargetVoicePart);
    return voicePart === null
      ? `Partition ${getTypeScoreLabel(score.Type).toLowerCase()}`
      : `Partition ${voicePart}`;
  });

  /** Versions publiées du même type et de la même voix cible — la bascule de `§ Partition`. */
  readonly siblingVersions = computed<IScore[]>(() => {
    const score = this.current();
    if (!score) return [];
    return this.scores().filter(
      candidate =>
        candidate.Id !== null &&
        candidate.Type === score.Type &&
        candidate.TargetVoicePart === score.TargetVoicePart
    );
  });

  readonly renderMode = computed<ScoreRenderMode>(() => {
    const fileName = this.current()?.OriginalFileName ?? '';
    const extension = fileName.split('.').pop()?.toLowerCase() ?? '';
    if (PDF_EXTENSIONS.includes(extension)) return 'pdf';
    if (IMAGE_EXTENSIONS.includes(extension)) return 'image';
    return 'unsupported';
  });

  /**
   * Seul `bypassSecurityTrustResourceUrl` légitime de l'application avec le chargement des
   * icônes : l'URL n'est PAS une donnée reçue — c'est un `blob:` fabriqué ici même par
   * `URL.createObjectURL` à partir d'un Blob que nous venons de télécharger. Angular refuse
   * toute valeur dans `iframe[src]` sans ce marquage explicite, quelle qu'en soit l'origine.
   * Le contenu lui-même reste confiné par l'attribut `sandbox` de l'iframe (voir le gabarit).
   */
  readonly safeResourceUrl = computed<SafeResourceUrl | null>(() => {
    const url = this.objectUrlSignal();
    return url === null ? null : this.sanitizer.bypassSecurityTrustResourceUrl(url);
  });

  readonly canDownload = computed(() => this.current()?.DownloadAllowed ?? false);

  constructor() {
    // `initialScoreId` est un input : absent à la construction. Il n'amorce que la première
    // sélection — une bascule de version ultérieure ne doit pas être écrasée par l'input.
    effect(() => {
      const initial = this.initialScoreId();
      untracked(() => {
        if (this.currentIdSignal() === null) this.currentIdSignal.set(initial);
      });
    });

    // Charge le fichier de la partition sélectionnée. Tout ce que fait load() est `untracked` :
    // il écrit dans des signaux que cet effet ne doit pas réobserver, sous peine de boucle.
    effect(() => {
      const score = this.current();
      untracked(() => this.load(score));
    });

    // Sans cette révocation, quitter la visionneuse laisse le Blob de la partition en mémoire
    // pour toute la durée de vie de l'onglet.
    this.destroyRef.onDestroy(() => this.revokeObjectUrl());
  }

  selectVersion(scoreId: string): void {
    this.currentIdSignal.set(scoreId);
  }

  close(): void {
    this.closed.emit();
  }

  download(): void {
    const score = this.current();
    if (!score?.Id || !score.DownloadAllowed) return;

    this.scoreService
      .download(score.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => triggerBlobDownload(blob, score.OriginalFileName ?? `partition-${score.Version}`),
        error: () => this.error.set('Impossible de télécharger cette partition.')
      });
  }

  private load(score: IScore | null): void {
    if (!score?.Id) {
      this.error.set('Partition introuvable.');
      return;
    }
    if (this.loadedScoreId() === score.Id) return;

    this.loading.set(true);
    this.error.set(null);

    const scoreId = score.Id;
    this.scoreService
      .download(scoreId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => {
          this.revokeObjectUrl();
          this.objectUrlSignal.set(URL.createObjectURL(blob));
          this.loadedScoreId.set(scoreId);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible d\'afficher cette partition. Merci de réessayer.');
        }
      });
  }

  private revokeObjectUrl(): void {
    const url = this.objectUrlSignal();
    if (url) URL.revokeObjectURL(url);
    this.objectUrlSignal.set(null);
  }
}
