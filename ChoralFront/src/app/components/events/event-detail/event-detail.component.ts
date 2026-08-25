import { effect, ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EventService } from '@app/services/events/event.service';
import { SongListService } from '@app/services/song-lists/song-list.service';
import { RecordingService } from '@app/services/recordings/recording.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { EventFormComponent } from '@app/components/events/event-form/event-form.component';
import { AudioPlayerComponent } from '@app/components/shared/audio-player/audio-player.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IEvent } from '@models/events-models/event.model';
import { ISongList } from '@models/song-lists-models/song-list.model';
import { IPlaylistTrack } from '@models/recordings-models/playlist-track.model';
import { IAudioTrack } from '@models/recordings-models/audio-track.model';
import { getEventTypeLabel } from '@app/enums/event-type.enum';
import { getTypeListLabel } from '@app/enums/type-list.enum';
import { SongListStatusEnum, getStatusListLabel } from '@app/enums/status-list.enum';
import { EventStatusEnum } from '@app/enums/event-status.enum';
import { EventEffectiveStateEnum, getEventEffectiveStateLabel } from '@app/enums/event-effective-state.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

const LISTS_PAGE_SIZE = 20;
const ALL_VOICE_PARTS: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];

// Liée via withComponentInputBinding() (app.config.ts) — jamais de paramMap.get() nu.
// L'id de route est validé (regex UUID) avant tout appel HTTP (OWASP A01).
// La création d'une liste de chants rattachée à cet événement se fait depuis la page
// générale /song-lists (SongListFormComponent n'accepte pas de préremplissage
// eventId dans ce lot — plan validé, input `songList?` uniquement).
@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [PaginationComponent, RouterLink, DatePipe, DataStateComponent, EventFormComponent, AudioPlayerComponent, IconComponent],
  templateUrl: './event-detail.component.html',
  styleUrl: './event-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventDetailComponent {
  private readonly eventService = inject(EventService);
  private readonly songListService = inject(SongListService);
  private readonly recordingService = inject(RecordingService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly getTypeListLabel = getTypeListLabel;
  protected readonly getStatusListLabel = getStatusListLabel;
  protected readonly getEventEffectiveStateLabel = getEventEffectiveStateLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allVoicePart = ALL_VOICE_PARTS;
  protected readonly SongListStatusEnum = SongListStatusEnum;
  protected readonly EventStatusEnum = EventStatusEnum;
  protected readonly EventEffectiveStateEnum = EventEffectiveStateEnum;

  readonly evt = signal<IEvent | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly editing = signal(false);

  readonly lists = signal<ISongList[]>([]);
  readonly listsTotalCount = signal(0);
  readonly listsLoading = signal(false);
  // Erreur propre au bloc « Listes de chants » : sans elle, l'échec remontait dans le signal
  // `error` de la page pendant que le bloc affichait quand même son état vide « Aucune liste
  // rattachée » — deux messages contradictoires pour un seul incident.
  readonly listsError = signal<string | null>(null);
  readonly listsPage = signal(1);

  readonly selectedVoicePart = signal<VoicePartEnum>(VoicePartEnum.Soprano);
  readonly playlist = signal<IPlaylistTrack[]>([]);
  readonly playlistLoading = signal(false);
  readonly playlistError = signal<string | null>(null);

  readonly listsTotalPages = computed(() => Math.max(1, Math.ceil(this.listsTotalCount() / LISTS_PAGE_SIZE)));

  // Projection playlist d'événement -> pistes du lecteur. Une piste vaut un chant pour cette
  // voix : le titre du chant identifie, la voix n'est qu'un rappel (elle est déjà choisie
  // au-dessus).
  readonly playlistTracks = computed<IAudioTrack[]>(() =>
    this.playlist().map(track => ({
      RecordingId: track.RecordingId,
      Title: track.SongTitle,
      Subtitle: track.TargetVoicePart === null ? null : getVoicePartLabel(track.TargetVoicePart),
      DurationSeconds: track.DurationSeconds
    }))
  );

  // Le back exclut délibérément les enregistrements de type Général d'une playlist par voix
  // (comportement testé côté RecordingService). Une playlist vide n'est donc pas une anomalie :
  // le message le dit, plutôt que de laisser croire à une panne de chargement.
  readonly playlistEmptyMessage = computed(
    () =>
      `Aucun enregistrement publié pour la voix ${getVoicePartLabel(this.selectedVoicePart())}. ` +
      `Les enregistrements de type « Général » n'apparaissent pas dans une playlist par voix.`
  );

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    return this.authStore.activeSpaceRoles().includes(UserRoleEnum.Manager);
  });

  constructor() {
    // Voir song-detail : `id` est un signal input, non peuplé à la construction.
    // load() gère déjà l'id absent/invalide (isValidUuid) — un garde ici bloquerait ce cas
    // et laisserait l'écran en chargement infini.
    effect(() => {
      this.load();
    });
  }

  onVoicePartChange(value: string): void {
    this.selectedVoicePart.set(Number(value) as VoicePartEnum);
    this.loadPlaylist();
  }

  goToListsPage(page: number): void {
    if (page < 1 || page > this.listsTotalPages()) return;
    this.listsPage.set(page);
    this.loadLists();
  }

  startEdit(): void {
    this.editing.set(true);
  }

  onFormSaved(updated: IEvent): void {
    this.evt.set(updated);
    this.editing.set(false);
  }

  onFormCancelled(): void {
    this.editing.set(false);
  }

  async deleteEvent(): Promise<void> {
    const current = this.evt();
    if (!current?.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cet événement',
      message: `« ${current.Title} » sera supprimé définitivement.`,
      impacts: [
        'Les réponses de participation associées sont perdues.',
        "Pour conserver l'historique, annulez l'événement au lieu de le supprimer."
      ],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.eventService
      .delete(current.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${current.Title} » a été supprimé.`);
          this.router.navigate(['/' + RoutePaths.Management, this.spaceId(), RoutePaths.Events]);
        },
        error: () => this.toast.error('Impossible de supprimer cet événement. Merci de réessayer.')
      });
  }

  // Publish exige un Lieu non vide côté back (400 sinon) — vérifié aussi ici pour désactiver
  // le bouton et éviter un aller-retour HTTP inutile (le back reste la source de vérité).
  publishEvent(): void {
    const current = this.evt();
    if (!current?.Id || !current.Location) return;
    this.changeStatus(current.Id, EventStatusEnum.Published, 'Impossible de publier cet événement.');
  }

  // Annulation : les participants voient l'événement passer en « Annulé ». Conséquence
  // visible par des tiers, donc confirmation explicite (Spec §6.4).
  async cancelEvent(): Promise<void> {
    const current = this.evt();
    if (!current?.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Annuler cet événement',
      message: `« ${current.Title} » sera marqué comme annulé.`,
      impacts: ["L'événement reste visible dans l'historique, avec son statut."],
      confirmationLabel: "Annuler l'événement",
      danger: true
    });
    if (!confirmed) return;

    this.changeStatus(current.Id, EventStatusEnum.Cancelled, "Impossible d'annuler cet événement.");
  }

  // Archive == transition finale (Draft/Publie/Annule -> Archive) : aucune transition
  // inverse n'est exposée par l'API, l'action n'est donc pas annulable — modale, et non
  // toast d'annulation (Spec §6.4).
  async archiveEvent(): Promise<void> {
    const current = this.evt();
    if (!current?.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Archiver cet événement',
      message: `« ${current.Title} » sortira des événements actifs.`,
      impacts: ["L'archivage est définitif : aucune transition ne permet de revenir en arrière."],
      confirmationLabel: 'Archiver',
      danger: true
    });
    if (!confirmed) return;

    this.changeStatus(current.Id, EventStatusEnum.Archived, "Impossible d'archiver cet événement.");
  }

  private changeStatus(id: string, status: EventStatusEnum, errorMessage: string): void {
    this.error.set(null);
    this.eventService
      .changeStatus(id, status)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => this.evt.set(updated),
        error: () => this.error.set(errorMessage)
      });
  }

  private load(): void {
    const eventId = this.id();
    if (!isValidUuid(eventId)) {
      this.loading.set(false);
      this.error.set("Identifiant d'événement invalide.");
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.eventService
      .getById(eventId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: evt => {
          this.evt.set(evt);
          this.loading.set(false);
          this.loadLists();
          this.loadPlaylist();
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cet événement.');
        }
      });
  }

  private loadLists(): void {
    const eventId = this.id();
    if (!isValidUuid(eventId)) return;

    this.listsLoading.set(true);
    this.listsError.set(null);

    this.songListService
      .getPaged({ Page: this.listsPage(), PageSize: LISTS_PAGE_SIZE, SortActive: 'Nom', SortDirection: 'asc' }, eventId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.lists.set(result.Items);
          this.listsTotalCount.set(result.TotalCount);
          this.listsLoading.set(false);
        },
        error: () => {
          this.listsLoading.set(false);
          this.listsError.set('Impossible de charger les listes de chants de cet événement.');
        }
      });
  }

  private loadPlaylist(): void {
    const eventId = this.id();
    if (!isValidUuid(eventId)) return;

    this.playlistLoading.set(true);
    this.playlistError.set(null);

    this.recordingService
      .getEventPlaylistByVoicePart(eventId, this.selectedVoicePart())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: tracks => {
          this.playlist.set(tracks);
          this.playlistLoading.set(false);
        },
        error: () => {
          this.playlistLoading.set(false);
          this.playlistError.set('Impossible de charger la playlist pour cette voix.');
        }
      });
  }
}
