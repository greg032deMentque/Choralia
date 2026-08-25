import { effect, ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { SongFormComponent } from '@app/components/songs/song-form/song-form.component';
import { SongInstructionsComponent } from '@app/components/songs/song-instructions/song-instructions.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISong } from '@models/songs-models/song.model';
import { getSongStatusLabel } from '@app/enums/song-status.enum';
import { getPrioritySongLabel } from '@app/enums/priority-song.enum';
import { getVoicePartLabel, getVoicePartsLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Liée via withComponentInputBinding() (app.config.ts) — jamais de paramMap.get() nu.
// L'id de route est validé (regex UUID) avant tout appel HTTP (OWASP A01).
@Component({
  selector: 'app-song-detail',
  standalone: true,
  imports: [RouterLink, DataStateComponent, SongFormComponent, SongInstructionsComponent, IconComponent],
  templateUrl: './song-detail.component.html',
  styleUrl: './song-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongDetailComponent {
  private readonly songService = inject(SongService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  // Voir SongListComponent : fallback '' pour ne jamais casser un routerLink hors contexte
  // de route (tests unitaires) — en consommation réel, garanti non-null par spaceRoleGuard.
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getSongStatusLabel = getSongStatusLabel;
  protected readonly getPrioritySongLabel = getPrioritySongLabel;
  protected readonly getVoicePartsLabel = getVoicePartsLabel;

  readonly song = signal<ISong | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly editing = signal(false);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    const roles = this.authStore.activeSpaceRoles();
    return roles.includes(UserRoleEnum.Manager) || roles.includes(UserRoleEnum.SectionLeader);
  });

  constructor() {
    // `id` est un signal input : il n'est peuplé qu'APRÈS la construction du composant.
    // Charger ici lisait donc toujours `undefined`, et l'écran affichait « Identifiant
    // invalide » quelle que soit l'URL. Un effect réagit à la valeur réelle, et recharge
    // aussi quand on navigue d'un détail à un autre sans détruire le composant.
    // load() gère déjà l'id absent/invalide (isValidUuid) — un garde ici bloquerait ce cas
    // et laisserait l'écran en chargement infini.
    effect(() => {
      this.load();
    });
  }

  missingVoicePartLabel(): string {
    return (this.song()?.VoicePartsWithoutPublishedRecording ?? []).map(getVoicePartLabel).join(', ');
  }

  startEdit(): void {
    this.editing.set(true);
  }

  onFormSaved(updated: ISong): void {
    this.song.set(updated);
    this.editing.set(false);
  }

  onFormCancelled(): void {
    this.editing.set(false);
  }

  async deleteSong(): Promise<void> {
    const current = this.song();
    if (!current?.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Archiver ce chant',
      message: `« ${current.Title} » quittera le répertoire actif de la chorale.`,
      impacts: ['Le chant reste consultable via le filtre « Archivé ».'],
      confirmationLabel: 'Archiver',
      danger: true
    });
    if (!confirmed) return;

    this.songService
      .delete(current.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${current.Title} » a été archivé.`);
          this.router.navigate(['/' + RoutePaths.Management, this.spaceId(), RoutePaths.Songs]);
        },
        error: () => this.toast.error("Impossible d'archiver ce chant. Merci de réessayer.")
      });
  }

  private load(): void {
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
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger ce chant.');
        }
      });
  }
}
