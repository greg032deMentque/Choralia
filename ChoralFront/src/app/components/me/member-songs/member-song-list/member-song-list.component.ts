import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { ISong } from '@models/songs-models/song.model';
import { SongStatusEnum } from '@app/enums/song-status.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { getVoicePartsLabel } from '@app/enums/voice-part.enum';

// Répertoire de la chorale active, en lecture seule, pour la zone /me. Volontairement sobre :
// un choriste consulte et écoute, il ne gère rien — ni création, ni édition, ni archivage, ni
// filtre de priorité. C'est ce qui la distingue de SongListComponent (/management), qui porte
// toute la gestion : les deux écrans ne partagent que leur service.
//
// Seuls les chants ACTIFS sont demandés : un chant archivé est sorti du répertoire, il n'a
// aucune raison d'apparaître dans la liste d'un membre.
@Component({
  selector: 'app-member-song-list',
  standalone: true,
  imports: [RouterLink, DataTableComponent, PageHeaderComponent],
  templateUrl: './member-song-list.component.html',
  styleUrl: './member-song-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MemberSongListComponent {
  private readonly songService = inject(SongService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getVoicePartsLabel = getVoicePartsLabel;

  private readonly tplTitle = viewChild<TemplateRef<{ $implicit: ISong }>>('tplTitle');
  private readonly tplVoiceParts = viewChild<TemplateRef<{ $implicit: ISong }>>('tplVoiceParts');

  readonly items = signal<ISong[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filterText = signal('');
  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>('Title');
  readonly sortDirection = signal<'asc' | 'desc' | undefined>('asc');

  // computed() plutôt qu'une valeur figée au constructeur : les viewChild(TemplateRef) ne sont
  // résolus qu'après la première passe de vue.
  readonly columns = computed<IDataTableColumn<ISong>[]>(() => [
    { key: 'Title', label: 'Titre', sortable: true, cellTemplate: this.tplTitle() },
    { key: 'VoiceParts', label: 'Voix concernées', cellTemplate: this.tplVoiceParts() },
    { key: 'Language', label: 'Langue', hideOnMobile: true },
    { key: 'WorkingKey', label: 'Tonalité', hideOnMobile: true }
  ]);

  constructor() {
    this.load();
  }

  onFilterTextChange(value: string): void {
    this.filterText.set(value);
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
    this.router.navigate(['/', RoutePaths.Me, RoutePaths.Songs, song.Id]);
  }

  load(): void {
    const choirId = this.authStore.activeSpaceId();
    // Un espace de type Événement n'a pas de répertoire propre : le dire, plutôt que d'appeler
    // GetPaged avec un ChoirId qui n'en est pas un et d'afficher une liste vide inexplicable.
    if (!choirId || this.authStore.activeSpaceType() !== SpaceTypeEnum.Choir) {
      this.error.set('Sélectionnez une chorale pour consulter son répertoire.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.songService
      .getPaged(
        { ChoirId: choirId, Status: SongStatusEnum.Active },
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
