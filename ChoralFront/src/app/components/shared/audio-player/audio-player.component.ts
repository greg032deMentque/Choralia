import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RecordingService } from '@app/services/recordings/recording.service';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAudioTrack } from '@models/recordings-models/audio-track.model';
import { formatDuration } from '@core/format-duration.util';
import { buildSequentialOrder, buildShuffledOrder, getNextPosition, getPreviousPosition } from './audio-player-navigation.util';

// Volume par défaut : lecture pleine puissance, le membre baisse s'il le souhaite. Nommé
// plutôt qu'écrit en dur dans deux endroits (initialisation du signal et de l'élément).
const DEFAULT_VOLUME = 1;

// Lecteur de playlist. Streaming authentifié via RecordingService.download() (Blob +
// ObjectURL) — jamais d'URL <audio src> nue : TokenInterceptor n'intercepte que les requêtes
// HttpClient, pas celles déclenchées nativement par l'élément <audio>.
//
// L'élément <audio> reste masqué et sans `controls` : le déplacement dans la piste est bien
// natif (le Blob est intégralement en mémoire, donc le seek est instantané et ne redemande
// rien au serveur), il ne manquait que les commandes. Elles sont reconstruites ici pour rester
// cohérentes avec le reste de l'interface (tokens, icônes du catalogue).
@Component({
  selector: 'app-audio-player',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './audio-player.component.html',
  styleUrl: './audio-player.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AudioPlayerComponent {
  private readonly recordingService = inject(RecordingService);
  private readonly destroyRef = inject(DestroyRef);

  readonly tracks = input.required<IAudioTrack[]>();
  /**
   * Message d'état vide. Il appartient à l'appelant : une playlist d'événement filtrée par voix
   * est vide pour une raison précise — le back exclut délibérément les enregistrements de type
   * Général d'une playlist par voix — et un message générique laisserait croire à une panne.
   */
  readonly emptyMessage = input<string>('Aucun enregistrement publié.');

  private readonly audioRef = viewChild<ElementRef<HTMLAudioElement>>('audioEl');

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly formatDuration = formatDuration;

  private readonly order = signal<number[]>([]);
  private readonly position = signal(0);
  private readonly loadedObjectUrl = signal<string | null>(null);
  private readonly loadedTrackId = signal<string | null>(null);
  // Échecs consécutifs de chargement : borne l'enchaînement automatique vers la piste suivante.
  // Sans elle, une playlist dont toutes les pistes échouent boucle indéfiniment sur elle-même.
  private readonly consecutiveFailures = signal(0);

  readonly loop = signal(false);
  readonly shuffle = signal(false);
  readonly isPlaying = signal(false);
  readonly isLoading = signal(false);
  readonly error = signal<string | null>(null);
  /**
   * Le navigateur a refusé `play()` faute de geste utilisateur (NotAllowedError). Cas réel sur
   * iOS/Safari : la lecture est déclenchée depuis le callback HTTP du téléchargement du Blob,
   * donc hors de la pile d'appel du clic. La piste est chargée et prête — il ne manque qu'un
   * appui, que ce drapeau demande explicitement au lieu d'afficher un lecteur figé en « lecture ».
   */
  readonly needsUserGesture = signal(false);

  readonly currentTime = signal(0);
  readonly duration = signal(0);
  readonly volume = signal(DEFAULT_VOLUME);

  readonly currentTrack = computed<IAudioTrack | null>(() => {
    const order = this.order();
    const idx = order[this.position()];
    const tracks = this.tracks();
    return idx !== undefined ? (tracks[idx] ?? null) : null;
  });

  readonly hasTracks = computed(() => this.tracks().length > 0);

  /** Pistes dans l'ordre de lecture réel : la liste affichée suit donc le mode aléatoire. */
  readonly orderedTracks = computed<IAudioTrack[]>(() => {
    const tracks = this.tracks();
    return this.order()
      .map(index => tracks[index])
      .filter((track): track is IAudioTrack => track !== undefined);
  });

  readonly currentPosition = this.position.asReadonly();

  // La barre de progression a besoin d'un maximum strictement positif : `duration` vaut 0 tant
  // que les métadonnées ne sont pas chargées, et un <input type="range"> dont max vaut 0 est
  // inutilisable. Repli sur la durée annoncée par la piste, connue avant tout chargement.
  readonly seekMax = computed(() => {
    const loaded = this.duration();
    if (loaded > 0) return loaded;
    return this.currentTrack()?.DurationSeconds ?? 0;
  });

  // Ne dépend que de `tracks` (nouvelle playlist assignée par le parent) — lit `shuffle`
  // via `untracked` pour ne pas déclencher cet effet à chaque toggleShuffle() (qui gère
  // déjà sa propre reconstruction d'ordre pour ne pas interrompre la lecture en cours).
  constructor() {
    effect(() => {
      const length = this.tracks().length;
      const shuffleNow = untracked(this.shuffle);
      this.order.set(shuffleNow ? buildShuffledOrder(length, null) : buildSequentialOrder(length));
      this.position.set(0);
      untracked(() => this.stop());
    });

    // stop() révoque l'ObjectURL de la piste en cours — sans ce hook, quitter la page en
    // cours de lecture laisserait l'URL (et le Blob associé) en mémoire indéfiniment.
    this.destroyRef.onDestroy(() => this.stop());
  }

  togglePlayPause(): void {
    const audio = this.audioRef()?.nativeElement;
    if (!audio) return;

    if (this.isPlaying()) {
      audio.pause();
      this.isPlaying.set(false);
      return;
    }

    const track = this.currentTrack();
    if (!track) return;

    if (this.loadedTrackId() === track.RecordingId) {
      this.startPlayback(audio);
    } else {
      this.loadAndPlay(track);
    }
  }

  next(): void {
    const nextPosition = getNextPosition(this.position(), this.order().length, this.loop());
    if (nextPosition === null) {
      this.stop();
      return;
    }
    this.playAt(nextPosition);
  }

  previous(): void {
    const previousPosition = getPreviousPosition(this.position(), this.order().length, this.loop());
    if (previousPosition === null) return;
    this.playAt(previousPosition);
  }

  /** Saut direct depuis la liste des pistes : sans elle, seule la piste courante est atteignable. */
  playAt(position: number): void {
    if (position < 0 || position >= this.order().length) return;
    this.consecutiveFailures.set(0);
    this.position.set(position);
    const track = this.currentTrack();
    if (track) this.loadAndPlay(track);
  }

  toggleLoop(): void {
    this.loop.update(v => !v);
  }

  toggleShuffle(): void {
    const currentIndex = this.order()[this.position()] ?? null;
    const nextShuffle = !this.shuffle();
    this.shuffle.set(nextShuffle);
    const newOrder = nextShuffle
      ? buildShuffledOrder(this.tracks().length, currentIndex)
      : buildSequentialOrder(this.tracks().length);
    this.order.set(newOrder);
    this.position.set(currentIndex === null ? 0 : Math.max(0, newOrder.indexOf(currentIndex)));
  }

  onSeek(value: string): void {
    const audio = this.audioRef()?.nativeElement;
    const seconds = Number(value);
    if (!audio || !Number.isFinite(seconds)) return;
    audio.currentTime = seconds;
    this.currentTime.set(seconds);
  }

  onVolumeChange(value: string): void {
    const level = Number(value);
    if (!Number.isFinite(level)) return;
    const audio = this.audioRef()?.nativeElement;
    if (audio) audio.volume = level;
    this.volume.set(level);
  }

  onTimeUpdate(): void {
    const audio = this.audioRef()?.nativeElement;
    if (audio) this.currentTime.set(audio.currentTime);
  }

  onLoadedMetadata(): void {
    const audio = this.audioRef()?.nativeElement;
    if (audio) this.duration.set(audio.duration);
  }

  onEnded(): void {
    this.next();
  }

  /**
   * Échec de DÉCODAGE de la piste déjà téléchargée (format refusé par le navigateur), par
   * opposition à l'échec de téléchargement traité dans loadAndPlay(). Les deux enchaînent sur
   * la piste suivante : une piste illisible ne doit pas arrêter toute la playlist.
   */
  onAudioError(): void {
    if (this.loadedTrackId() === null) return;
    this.handleTrackFailure();
  }

  private loadAndPlay(track: IAudioTrack): void {
    this.isLoading.set(true);
    this.needsUserGesture.set(false);
    // L'avertissement « piste illisible » est conservé pendant le chargement de la piste
    // suivante — c'est justement le moment où il informe. Il n'est levé qu'à la lecture
    // effective (startPlayback), sinon il disparaîtrait avant d'avoir été lu.
    if (this.consecutiveFailures() === 0) this.error.set(null);

    this.recordingService
      .download(track.RecordingId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: blob => {
          const previousUrl = this.loadedObjectUrl();
          if (previousUrl) URL.revokeObjectURL(previousUrl);

          const url = URL.createObjectURL(blob);
          this.loadedObjectUrl.set(url);
          this.loadedTrackId.set(track.RecordingId);
          this.currentTime.set(0);
          this.duration.set(0);
          this.isLoading.set(false);

          const audio = this.audioRef()?.nativeElement;
          if (audio) {
            audio.src = url;
            audio.volume = this.volume();
            this.startPlayback(audio);
          }
        },
        error: () => {
          this.isLoading.set(false);
          this.handleTrackFailure();
        }
      });
  }

  /**
   * `play()` renvoie une Promise : elle est REJETÉE quand le navigateur refuse la lecture faute
   * de geste utilisateur. L'ignorer laissait `isPlaying` à true alors que rien ne jouait.
   * `Promise.resolve()` couvre en plus les environnements sans moteur média, où `play()` ne
   * renvoie pas de Promise du tout.
   */
  private startPlayback(audio: HTMLAudioElement): void {
    this.needsUserGesture.set(false);

    Promise.resolve(audio.play()).then(
      () => {
        this.isPlaying.set(true);
        this.consecutiveFailures.set(0);
        this.error.set(null);
      },
      (reason: unknown) => {
        this.isPlaying.set(false);
        if (reason instanceof DOMException && reason.name === 'NotAllowedError') {
          this.needsUserGesture.set(true);
          return;
        }
        this.handleTrackFailure();
      }
    );
  }

  // Une piste en échec ne doit pas arrêter la playlist : on enchaîne sur la suivante, en
  // bornant la cascade au nombre de pistes pour ne pas boucler quand toutes échouent.
  private handleTrackFailure(): void {
    this.isPlaying.set(false);
    const failures = this.consecutiveFailures() + 1;
    this.consecutiveFailures.set(failures);

    if (failures >= this.order().length) {
      this.stop();
      this.consecutiveFailures.set(0);
      this.error.set('Aucune piste de cette playlist n\'a pu être lue. Merci de réessayer plus tard.');
      return;
    }

    this.error.set('Piste illisible, passage à la suivante.');

    const nextPosition = getNextPosition(this.position(), this.order().length, true);
    if (nextPosition === null) {
      this.stop();
      return;
    }
    this.position.set(nextPosition);
    const track = this.currentTrack();
    if (track) this.loadAndPlay(track);
  }

  private stop(): void {
    const audio = this.audioRef()?.nativeElement;
    if (audio) {
      audio.pause();
      audio.removeAttribute('src');
    }
    const previousUrl = this.loadedObjectUrl();
    if (previousUrl) URL.revokeObjectURL(previousUrl);
    this.loadedObjectUrl.set(null);
    this.loadedTrackId.set(null);
    this.isPlaying.set(false);
    this.needsUserGesture.set(false);
    this.currentTime.set(0);
    this.duration.set(0);
  }
}
