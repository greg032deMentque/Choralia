import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AdminEventService } from '@app/services/admin/admin-event.service';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminEventDetail } from '@models/admin-models/admin-event-detail.model';
import { getEventTypeLabel } from '@app/enums/event-type.enum';
import { EventStatusEnum, getEventStatusLabel } from '@app/enums/event-status.enum';
import { getEventEffectiveStateLabel } from '@app/enums/event-effective-state.enum';

// Fiche en lecture seule (`10-D23`, décision produit : AdminEvenementController n'expose aucune
// écriture — la management réelle d'un événement reste sur EventService, côté chorale).
@Component({
  selector: 'app-event-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, DataStateComponent, IconComponent],
  templateUrl: './event-detail.component.html',
  styleUrl: './event-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventDetailComponent {
  private readonly adminEventService = inject(AdminEventService);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly EventStatusEnum = EventStatusEnum;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly getEventStatusLabel = getEventStatusLabel;
  protected readonly getEventEffectiveStateLabel = getEventEffectiveStateLabel;

  readonly detail = signal<IAdminEventDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      this.load();
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

    this.adminEventService
      .getById(eventId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          this.detail.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cet événement.');
        }
      });
  }
}
