import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { AdminChoirService } from '@app/services/admin/admin-choir.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { DataTableComponent, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminChoirDetail } from '@models/admin-models/admin-choir-detail.model';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { ISong } from '@models/songs-models/song.model';
import { IEvent } from '@models/events-models/event.model';
import { ChoirStatusEnum, getStatusChoirLabel, getStatusChoirTransitionsAllowed } from '@app/enums/status-choir.enum';
import { getMemberStatusLabel } from '@app/enums/member-status.enum';
import { getUserRolesLabel } from '@app/enums/user-role.enum';
import { getVoicePartLabel } from '@app/enums/voice-part.enum';
import { getSongStatusLabel } from '@app/enums/song-status.enum';
import { getPrioritySongLabel } from '@app/enums/priority-song.enum';
import { getEventTypeLabel } from '@app/enums/event-type.enum';
import { getEventStatusLabel } from '@app/enums/event-status.enum';
import { formatBytes, isUsageCritical, percentageUsage } from '@app/services/admin/format-bytes.util';

const SUB_TAB_PAGE_SIZE = 10;

export type ChoirDetailTab = 'information' | 'members' | 'songs' | 'events' | 'limits';

// Fiche transverse d'une chorale (administration générale). L'Admin lit tout (membres, chants,
// événements) mais n'écrit que sur les informations (Nom/Description) et le statut — jamais sur
// le contenu (`10-D23`, décision produit). Les 3 onglets de contenu sont chargés paresseusement
// (au premier clic sur l'onglet), pas au chargement de la fiche : éviter 4 requêtes HTTP
// systématiques alors que la plupart des consultations ne portent que sur les informations.
//
// Aucun whitelist de tri n'a été fourni pour GetMembers/GetSongs/GetEvents (délégation
// directe aux services existants, contrairement à GetPaged qui documente ClientsColonnesTriables) —
// décision assumée : ces 3 tableaux restent non triables (filtre + pagination uniquement),
// plutôt que de déclarer sortable=true sur un champ sans garantie de support serveur.
@Component({
  selector: 'app-choir-detail',
  standalone: true,
  imports: [
    RouterLink,
    DatePipe,
    ReactiveFormsModule,
    DataStateComponent,
    DataTableComponent,
    FormFieldComponent,
    SubmitOnceDirective,
    IconComponent
  ],
  templateUrl: './choir-detail.component.html',
  styleUrl: './choir-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChoirDetailComponent {
  private readonly adminChoirService = inject(AdminChoirService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  // Lié via withComponentInputBinding() — jamais de paramMap.get() nu.
  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly ChoirStatusEnum = ChoirStatusEnum;
  protected readonly getStatusChoirLabel = getStatusChoirLabel;
  protected readonly getMemberStatusLabel = getMemberStatusLabel;
  protected readonly getUserRolesLabel = getUserRolesLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getSongStatusLabel = getSongStatusLabel;
  protected readonly getPrioritySongLabel = getPrioritySongLabel;
  protected readonly getEventTypeLabel = getEventTypeLabel;
  protected readonly getEventStatusLabel = getEventStatusLabel;
  protected readonly formatBytes = formatBytes;
  protected readonly isUsageCritical = isUsageCritical;
  protected readonly percentageUsage = percentageUsage;

  readonly detail = signal<IAdminChoirDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly editingInformations = signal(false);
  readonly activeTab = signal<ChoirDetailTab>('information');

  readonly transitionsAllowed = computed(() => {
    const current = this.detail();
    return current ? getStatusChoirTransitionsAllowed(current.Status) : [];
  });

  readonly form = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    description: this.fb.nonNullable.control('', [Validators.maxLength(500)])
  });

  // --- Onglet Membres (lecture seule)
  private readonly tplMemberRoles = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplMemberRoles');
  private readonly tplMemberVoicePart = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplMemberVoicePart');
  private readonly tplMemberStatus = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplMemberStatus');
  readonly membersColumns = computed<IDataTableColumn<IMemberChoir>[]>(() => [
    { key: 'UserFullName', label: 'Nom' },
    { key: 'UserEmail', label: 'Email' },
    { key: 'Roles', label: 'Rôle(s)', cellTemplate: this.tplMemberRoles() },
    { key: 'SectionVoicePart', label: 'Voix', cellTemplate: this.tplMemberVoicePart() },
    { key: 'Status', label: 'Statut', cellTemplate: this.tplMemberStatus() }
  ]);
  readonly membersItems = signal<IMemberChoir[]>([]);
  readonly membersTotalCount = signal(0);
  readonly membersLoading = signal(false);
  readonly membersPage = signal(1);
  readonly membersFilterText = signal('');
  private membersLoaded = false;

  // --- Onglet Chants (lecture seule)
  private readonly tplSongStatus = viewChild<TemplateRef<{ $implicit: ISong }>>('tplSongStatus');
  private readonly tplSongPriority = viewChild<TemplateRef<{ $implicit: ISong }>>('tplSongPriority');
  readonly songsColumns = computed<IDataTableColumn<ISong>[]>(() => [
    { key: 'Title', label: 'Titre' },
    { key: 'Author', label: 'Auteur' },
    { key: 'Status', label: 'Statut', cellTemplate: this.tplSongStatus() },
    { key: 'Priority', label: 'Priorité', cellTemplate: this.tplSongPriority() }
  ]);
  readonly songsItems = signal<ISong[]>([]);
  readonly songsTotalCount = signal(0);
  readonly songsLoading = signal(false);
  readonly songsPage = signal(1);
  readonly songsFilterText = signal('');
  private songsLoaded = false;

  // --- Onglet Événements (lecture seule)
  private readonly tplEventDate = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplEvenementDate');
  private readonly tplEventType = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplEvenementType');
  private readonly tplEventStatus = viewChild<TemplateRef<{ $implicit: IEvent }>>('tplEvenementStatut');
  readonly eventsColumns = computed<IDataTableColumn<IEvent>[]>(() => [
    { key: 'Title', label: 'Titre' },
    { key: 'StartDate', label: 'Date', cellTemplate: this.tplEventDate() },
    { key: 'Type', label: 'Type', cellTemplate: this.tplEventType() },
    { key: 'Location', label: 'Lieu' },
    { key: 'Status', label: 'Statut', cellTemplate: this.tplEventStatus() }
  ]);
  readonly eventsItems = signal<IEvent[]>([]);
  readonly eventsTotalCount = signal(0);
  readonly eventsLoading = signal(false);
  readonly eventsPage = signal(1);
  readonly eventsFilterText = signal('');
  private eventsLoaded = false;

  constructor() {
    // `id` est un signal input, non peuplé à la construction (voir user-detail) —
    // load() gère déjà l'id absent/invalide.
    effect(() => {
      this.load();
    });
  }

  selectTab(tab: ChoirDetailTab): void {
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);

    if (tab === 'members' && !this.membersLoaded) this.loadMembers();
    if (tab === 'songs' && !this.songsLoaded) this.loadSongs();
    if (tab === 'events' && !this.eventsLoaded) this.loadEvents();
  }

  startEditInformations(): void {
    const current = this.detail();
    if (!current) return;
    this.form.reset({ name: current.Name, description: current.Description ?? '' });
    this.editingInformations.set(true);
  }

  cancelEditInformations(): void {
    this.editingInformations.set(false);
  }

  submitInformations = (): Observable<IAdminChoirDetail> => {
    const current = this.detail();
    if (this.form.invalid || !current) {
      this.form.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.error.set(null);
    const raw = this.form.getRawValue();

    return this.adminChoirService.update({ Id: current.Id, Name: raw.name, Description: raw.description || null }).pipe(
      tap(updated => {
        this.detail.set(updated);
        this.editingInformations.set(false);
        this.toastService.success('Informations mises à jour.');
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          this.error.set("Impossible d'enregistrer les modifications. Merci de réessayer.");
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  // Une fonction par transition target, liée dynamiquement via [appSubmitOnce]="statusAction(target)"
  // dans le gabarit — seules les transitions de transitionsAllowed() sont proposées.
  statusAction(target: ChoirStatusEnum): () => Observable<IAdminChoirDetail> {
    return () => this.changeStatusTo(target);
  }

  onMembersFilterChange(value: string): void {
    this.membersFilterText.set(value);
    this.membersPage.set(1);
    this.loadMembers();
  }

  onMembersPageChange(page: number): void {
    this.membersPage.set(page);
    this.loadMembers();
  }

  onSongsFilterChange(value: string): void {
    this.songsFilterText.set(value);
    this.songsPage.set(1);
    this.loadSongs();
  }

  onSongsPageChange(page: number): void {
    this.songsPage.set(page);
    this.loadSongs();
  }

  onEventsFilterChange(value: string): void {
    this.eventsFilterText.set(value);
    this.eventsPage.set(1);
    this.loadEvents();
  }

  onEventsPageChange(page: number): void {
    this.eventsPage.set(page);
    this.loadEvents();
  }

  private changeStatusTo(target: ChoirStatusEnum): Observable<IAdminChoirDetail> {
    const current = this.detail();
    if (!current) return throwError(() => new Error('no-detail'));
    this.error.set(null);

    if (target === ChoirStatusEnum.Archived) {
      return this.adminChoirService.getImpactArchivage(current.Id).pipe(
        switchMap(impact =>
          from(
            this.confirmService.confirm({
              title: 'Archiver cette chorale ?',
              message: `La chorale « ${current.Name} » sera archivée : son contenu est conservé mais devient invisible des membres.`,
              impacts: [
                `${impact.MemberCount} membre(s)`,
                `${impact.SongCount} chant(s)`,
                `${impact.EventCount} événement(s)`
              ],
              danger: true,
              confirmationLabel: 'Archiver'
            })
          )
        ),
        switchMap(confirmed =>
          confirmed ? this.adminChoirService.changeStatus({ Id: current.Id, Status: target }) : throwError(() => new Error('cancelled'))
        ),
        tap(updated => {
          this.detail.set(updated);
          this.toastService.success('Chorale archivée.');
        }),
        catchError((err: unknown) => this.handleStatusError(err)),
        takeUntilDestroyed(this.destroyRef)
      );
    }

    return from(
      this.confirmService.confirm({
        title: `Passer au statut « ${getStatusChoirLabel(target)} » ?`,
        message: `La chorale « ${current.Name} » passera du statut « ${getStatusChoirLabel(current.Status)} » à « ${getStatusChoirLabel(target)} ».`,
        confirmationLabel: 'Confirmer'
      })
    ).pipe(
      switchMap(confirmed =>
        confirmed ? this.adminChoirService.changeStatus({ Id: current.Id, Status: target }) : throwError(() => new Error('cancelled'))
      ),
      tap(updated => {
        this.detail.set(updated);
        this.toastService.success('Statut mis à jour.');
      }),
      catchError((err: unknown) => this.handleStatusError(err)),
      takeUntilDestroyed(this.destroyRef)
    );
  }

  private handleStatusError(err: unknown): Observable<never> {
    if (err instanceof Error && err.message === 'cancelled') {
      return throwError(() => err);
    }
    if (err instanceof HttpErrorResponse && (err.status === 409 || err.status === 400)) {
      const message = (err.error as { Message?: string } | null)?.Message;
      this.error.set(message ?? 'Transition de statut impossible.');
    } else {
      this.error.set('Impossible de mettre à jour le statut de cette chorale. Merci de réessayer.');
    }
    return throwError(() => err);
  }

  private load(): void {
    const choirId = this.id();
    if (!isValidUuid(choirId)) {
      this.loading.set(false);
      this.error.set('Identifiant de chorale invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.adminChoirService
      .getById(choirId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          this.detail.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cette chorale.');
        }
      });
  }

  private loadMembers(): void {
    const choirId = this.id();
    if (!isValidUuid(choirId)) return;

    this.membersLoading.set(true);
    this.adminChoirService
      .getMembers(choirId, {
        Page: this.membersPage(),
        PageSize: SUB_TAB_PAGE_SIZE,
        Filter: this.membersFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.membersLoaded = true;
          this.membersItems.set(result.Items);
          this.membersTotalCount.set(result.TotalCount);
          this.membersLoading.set(false);
        },
        error: () => {
          this.membersLoading.set(false);
          this.error.set('Impossible de charger les membres de cette chorale.');
        }
      });
  }

  private loadSongs(): void {
    const choirId = this.id();
    if (!isValidUuid(choirId)) return;

    this.songsLoading.set(true);
    this.adminChoirService
      .getSongs(choirId, {
        Page: this.songsPage(),
        PageSize: SUB_TAB_PAGE_SIZE,
        Filter: this.songsFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.songsLoaded = true;
          this.songsItems.set(result.Items);
          this.songsTotalCount.set(result.TotalCount);
          this.songsLoading.set(false);
        },
        error: () => {
          this.songsLoading.set(false);
          this.error.set('Impossible de charger les chants de cette chorale.');
        }
      });
  }

  private loadEvents(): void {
    const choirId = this.id();
    if (!isValidUuid(choirId)) return;

    this.eventsLoading.set(true);
    this.adminChoirService
      .getEvents(choirId, {
        Page: this.eventsPage(),
        PageSize: SUB_TAB_PAGE_SIZE,
        Filter: this.eventsFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.eventsLoaded = true;
          this.eventsItems.set(result.Items);
          this.eventsTotalCount.set(result.TotalCount);
          this.eventsLoading.set(false);
        },
        error: () => {
          this.eventsLoading.set(false);
          this.error.set('Impossible de charger les événements de cette chorale.');
        }
      });
  }
}
