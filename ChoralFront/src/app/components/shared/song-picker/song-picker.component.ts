import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, output, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths, managementPath } from '@core/route-paths';
import { ISelectOption } from '@models/common-models/select-option.model';

let nextSelectId = 0;

/**
 * Sélecteur du chant porteur, pour les écrans dont le contenu s'attache TOUJOURS à un chant
 * (Partitions, Enregistrements). Il porte les deux états du répertoire, indissociables :
 * un chant est sélectionnable, ou le répertoire est vide — et un répertoire vide est une
 * impasse qui doit nommer sa cause et donner sa sortie, jamais un `<select>` muet.
 *
 * Extrait de ScoreListComponent et RecordingListComponent, où le chargement des options, le
 * drapeau « déjà chargé », le calcul du répertoire vide et le balisage étaient dupliqués à
 * l'identique — le message ne différait que d'un mot.
 */
@Component({
  selector: 'app-song-picker',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './song-picker.component.html',
  styleUrl: './song-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongPickerComponent {
  private readonly songService = inject(SongService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  /**
   * Ce que l'écran appelant attache au chant, article compris — « une partition »,
   * « un enregistrement ». Fragment déjà accordé plutôt que nom + règle de grammaire : la
   * seule chose que ce composant ne peut pas déduire, c'est le vocabulaire de l'appelant.
   */
  readonly attachableLabel = input.required<string>();

  /** Émis au chargement (premier chant présélectionné) puis à chaque changement de l'utilisateur. */
  readonly songSelected = output<string | null>();
  /** Permet à l'appelant de taire ses propres messages de liste quand il n'y a rien à choisir. */
  readonly repertoireEmpty = output<boolean>();
  /**
   * L'appelant reste maître de son affichage d'erreur (état inline, jamais un toast ici).
   * `output()` sans paramètre de type plutôt que `output<void>()` : même signature d'emit, mais
   * sans déclencher `no-invalid-void-type` (même convention que `toggleNav` de la topbar).
   */
  readonly loadFailed = output();

  // Identifiant unique : deux sélecteurs sur une même page produiraient un `for`/`id` ambigu,
  // donc un label qui n'active plus le bon champ.
  protected readonly selectId = `songPicker-${nextSelectId++}`;

  protected readonly options = signal<ISelectOption<string>[]>([]);
  protected readonly selectedSongId = signal<string | null>(null);

  // Distingue « pas encore chargé » de « répertoire réellement vide » : sans ce drapeau, un
  // sélecteur vide pendant le chargement afficherait à tort l'impasse.
  private readonly loaded = signal(false);
  protected readonly hasNoSong = computed(() => this.loaded() && this.options().length === 0);

  protected readonly songsLink = computed(() =>
    managementPath(this.authStore.activeSpaceId() ?? '', RoutePaths.Songs)
  );

  constructor() {
    this.load();
  }

  protected onChange(value: string): void {
    const songId = value || null;
    this.selectedSongId.set(songId);
    this.songSelected.emit(songId);
  }

  private load(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) return;

    this.songService
      .getChoirOptions(choirId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: options => {
          this.options.set(options);
          this.loaded.set(true);
          this.repertoireEmpty.emit(options.length === 0);

          if (options.length > 0) {
            this.selectedSongId.set(options[0].Value);
            this.songSelected.emit(options[0].Value);
          }
        },
        error: () => this.loadFailed.emit()
      });
  }
}
