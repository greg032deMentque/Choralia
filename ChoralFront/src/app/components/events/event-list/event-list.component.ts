import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EventService } from '@app/services/events/event.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths } from '@core/route-paths';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { EventFormComponent } from '@app/components/events/event-form/event-form.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IEvent } from '@models/events-models/event.model';
import { getEventTypeLabel } from '@app/enums/event-type.enum';
import { EventEffectiveStateEnum, getEventEffectiveStateLabel } from '@app/enums/event-effective-state.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';

// Liste paginée des événements de la chorale active. Management (création/édition/suppression)
// réservée au rôle Responsable — pas de délégation SectionLeader pour les événements
// (bloc de transfert, nuance rôles).
@Component({
  selector: 'app-event-list',
  standalone: true,
  imports: [RouterLink, DatePipe, DataTableComponent, PageHeaderComponent, EventFormComponent, IconComponent],
  templateUrl: './event-list.component.html',
  styleUrl: './event-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class EventListComponent {
  private readonly eventService = inject(EventService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly RoutePaths = RoutePaths;
  protected readonly spaceId = computed(() => this.authStore.activeSpaceId() ?? '');
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly getEventEffectiveStateLabel = getEventEffectiveStateLabel;
  protected readonly EventEffectiveStateEnum = EventEffectiveStateEnum;

  private readonly tplTitle = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplTitle');
  private readonly tplType = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplType');
  private readonly tplStartDate = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplStartDate');
  private readonly tplEndDate = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplEndDate');
  private readonly tplState = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplState');
  private readonly tplActions = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplActions');

  readonly items = signal<IEvent[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filterText = signal('');

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly showForm = signal(false);
  readonly editingEvent = signal<IEvent | null>(null);

  protected readonly canManage = computed(() => {
    if (this.authStore.isGlobalAdmin()) return true;
    return this.authStore.activeSpaceRoles().includes(UserRoleEnum.Manager);
  });

  // La date de fin est masquée en rendu carte : elle n'est pas nécessaire pour reconnaître
  // un événement dans une liste, et la carte reste lisible sans elle.
  readonly columns = computed<IDataTableColumn<IEvent>[]>(() => [
    { key: 'Title', label: 'Titre', sortable: true, cellTemplate: this.tplTitle() },
    { key: 'Type', label: 'Type', sortable: true, cellTemplate: this.tplType() },
    { key: 'StartDate', label: 'Date de début', sortable: true, cellTemplate: this.tplStartDate() },
    { key: 'EndDate', label: 'Date de fin', cellTemplate: this.tplEndDate(), hideOnMobile: true },
    { key: 'EffectiveState', label: 'État', cellTemplate: this.tplState() },
    { key: 'Actions', label: 'Actions', cellTemplate: this.tplActions() }
  ]);

  constructor() {
    this.load();
  }

  // L'anti-rebond de la recherche est porté par DataTableComponent.
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

  openEvent(evt: IEvent): void {
    if (!evt.Id) return;
    this.router.navigate(['/', RoutePaths.Management, this.spaceId(), RoutePaths.Events, evt.Id]);
  }

  openCreateForm(): void {
    this.editingEvent.set(null);
    this.showForm.set(true);
  }

  openEditForm(evt: IEvent): void {
    this.editingEvent.set(evt);
    this.showForm.set(true);
  }

  onFormSaved(): void {
    this.showForm.set(false);
    this.editingEvent.set(null);
    this.load();
  }

  onFormCancelled(): void {
    this.showForm.set(false);
    this.editingEvent.set(null);
  }

  // Suppression définitive (et non annulation de l'événement, qui est une transition d'état
  // distincte) : mot-clé de confirmation à saisir, conformément au traitement des actions
  // irréversibles (10-D42).
  async deleteEvent(evt: IEvent): Promise<void> {
    if (!evt.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cet événement',
      message: `« ${evt.Title} » sera supprimé définitivement.`,
      impacts: [
        'Les réponses de participation associées sont perdues.',
        "Pour conserver l'historique, annulez l'événement au lieu de le supprimer."
      ],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.eventService
      .delete(evt.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success(`« ${evt.Title} » a été supprimé.`);
          this.load();
        },
        error: () => this.toast.error('Impossible de supprimer cet événement. Merci de réessayer.')
      });
  }

  private load(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) {
      this.error.set('Aucune chorale active sélectionnée.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.eventService
      .getPaged(choirId, {
        Page: this.page(),
        PageSize: this.pageSize(),
        SortActive: this.sortActive(),
        SortDirection: this.sortDirection(),
        Filter: this.filterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.items.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les événements. Merci de réessayer.');
        }
      });
  }
}
