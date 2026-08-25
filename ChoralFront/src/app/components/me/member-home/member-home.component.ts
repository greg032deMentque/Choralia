import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthStore } from '@core/auth.store';
import { EventService } from '@app/services/events/event.service';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IEvent } from '@models/events-models/event.model';
import { getEventTypeLabel } from '@app/enums/event-type.enum';
import { getVoicePartLabel } from '@app/enums/voice-part.enum';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { RoutePaths } from '@core/route-paths';

// Un seul événement affiché : « le prochain », pas un agenda. Le back trie et filtre
// (Upcoming + SortActive=StartDate), le front ne fait aucun tri côté client.
const NEXT_EVENT_PAGE_SIZE = 1;

// Écran d'accueil de la zone /me — les contenus utiles au membre précèdent les informations
// de profil : prochain événement, accès au répertoire, puis rattachements et identité.
@Component({
  selector: 'app-member-home',
  standalone: true,
  imports: [DatePipe, RouterLink, DataStateComponent, IconComponent, PageHeaderComponent],
  templateUrl: './member-home.component.html',
  styleUrl: './member-home.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MemberHomeComponent {
  private readonly authStore = inject(AuthStore);
  private readonly eventService = inject(EventService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly RoutePaths = RoutePaths;

  readonly user = this.authStore.user;

  readonly fullName = computed(() => {
    const user = this.user();
    if (!user) return '';
    return `${user.Firstname} ${user.Lastname}`.trim();
  });

  // Seuls les espaces de type Chorale sont présentés comme « mes chorales » : un espace de
  // type Événement est un rattachement ponctuel, pas une appartenance à afficher ici.
  readonly choirs = computed(() =>
    this.authStore.spaceRoles().filter(space => space.SpaceType === SpaceTypeEnum.Choir)
  );
  readonly canBrowseSongs = computed(() => this.authStore.activeSpaceType() === SpaceTypeEnum.Choir);

  readonly nextEvent = signal<IEvent | null>(null);
  readonly loadingNextEvent = signal(false);
  readonly nextEventError = signal<string | null>(null);

  constructor() {
    this.loadNextEvent();
  }

  loadNextEvent(): void {
    this.loadingNextEvent.set(true);
    this.nextEventError.set(null);

    this.eventService
      .getUpcoming({ Page: 1, PageSize: NEXT_EVENT_PAGE_SIZE, SortActive: 'StartDate', SortDirection: 'asc' })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.loadingNextEvent.set(false);
          this.nextEvent.set(result.Items[0] ?? null);
        },
        error: () => {
          this.loadingNextEvent.set(false);
          this.nextEventError.set('Impossible de charger votre prochain événement. Merci de réessayer.');
        }
      });
  }
}
