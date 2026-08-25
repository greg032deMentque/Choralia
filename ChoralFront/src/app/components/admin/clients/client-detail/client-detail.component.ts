import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { ClientService } from '@app/services/admin/client.service';
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
import { IClient } from '@models/admin-models/client.model';
import { IClientChoirListItem } from '@models/admin-models/client-choir-list-item.model';
import { ClientStatusEnum, getStatusClientLabel } from '@app/enums/status-client.enum';
import { formatBytes, isUsageCritical, percentageUsage } from '@app/services/admin/format-bytes.util';

const CHOIRS_PAGE_SIZE = 10;
const BYTES_PER_GB = 1024 ** 3;
const BYTES_PER_MB = 1024 ** 2;

export type ClientDetailTab = 'information' | 'limits' | 'choirs' | 'managers';

// Fiche client (administration générale). Suspendre est l'action la plus lourde de toute
// l'administration (coupe l'accès à toutes les chorales du client) : ConfirmService avec
// impacts chiffrés (ImpactSuspension) ET motCleConfirmation = nom du client. Réactiver passe par
// une route dédiée (POST /Reactivate, pas ChangeStatus) — seule capable de vérifier les plafonds
// avant de rouvrir l'accès (voir ClientService.ReactivateAsync côté back).
//
// Écart assumé (aucun endpoint ne le fournit) : il n'existe aucune route GET pour lister les
// responsables actuels d'un client (ClientController n'expose que POST/DELETE Responsables).
// L'onglet Responsables ne peut donc offrir que la désignation (par email) et le retrait (par
// identifiant utilisateur, à connaître par ailleurs — ex. fiche utilisateur de la zone
// /admin/users) — pas d'affichage de la liste actuelle. Documenté explicitement à
// l'écran plutôt que silencieux.
@Component({
  selector: 'app-client-detail',
  standalone: true,
  imports: [
    RouterLink,
    ReactiveFormsModule,
    DataStateComponent,
    DataTableComponent,
    FormFieldComponent,
    SubmitOnceDirective,
    IconComponent
  ],
  templateUrl: './client-detail.component.html',
  styleUrl: './client-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientDetailComponent {
  private readonly clientService = inject(ClientService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly ClientStatusEnum = ClientStatusEnum;
  protected readonly getStatusClientLabel = getStatusClientLabel;
  protected readonly formatBytes = formatBytes;
  protected readonly isUsageCritical = isUsageCritical;
  protected readonly percentageUsage = percentageUsage;

  readonly detail = signal<IClient | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly activeTab = signal<ClientDetailTab>('information');

  readonly editingInformations = signal(false);
  readonly editingLimits = signal(false);

  readonly informationsForm = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    contactName: this.fb.nonNullable.control('', [Validators.maxLength(150)]),
    contactEmail: this.fb.nonNullable.control('', [Validators.email, Validators.maxLength(256)])
  });

  // Plafonds édités en Go/Mo (plus lisible qu'un champ en octets bruts) — convertis en octets
  // uniquement au moment de l'envoi (voir submitLimits).
  readonly limitsForm = this.fb.nonNullable.group({
    limitChoirs: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]),
    limitMembers: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]),
    quotaStorageGo: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)]),
    maxFileSizeMb: this.fb.nonNullable.control(0, [Validators.required, Validators.min(0)])
  });

  // --- Onglet Responsables (désignation/retrait uniquement — voir écart assumé ci-dessus)
  readonly assignForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)])
  });
  readonly removeForm = this.fb.nonNullable.group({
    userId: this.fb.nonNullable.control('', [Validators.required])
  });
  readonly assignError = signal<string | null>(null);
  readonly removeError = signal<string | null>(null);

  // --- Onglet Chorales (lecture — la management réelle se fait depuis /admin/chorales)
  readonly choirsColumns: IDataTableColumn<IClientChoirListItem>[] = [
    { key: 'Name', label: 'Nom', sortable: true },
    { key: 'MemberCount', label: 'Membres' },
    { key: 'SongCount', label: 'Chants' },
    { key: 'UpcomingEventCount', label: 'Événements à venir' }
  ];
  readonly choirsItems = signal<IClientChoirListItem[]>([]);
  readonly choirsTotalCount = signal(0);
  readonly choirsLoading = signal(false);
  readonly choirsPage = signal(1);
  readonly choirsSortActive = signal<string | undefined>(undefined);
  readonly choirsSortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly choirsFilterText = signal('');
  private choirsLoaded = false;

  constructor() {
    effect(() => {
      this.load();
    });
  }

  selectTab(tab: ClientDetailTab): void {
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);
    if (tab === 'choirs' && !this.choirsLoaded) this.loadChoirs();
  }

  // --- Informations
  startEditInformations(): void {
    const current = this.detail();
    if (!current) return;
    this.informationsForm.reset({ name: current.Name, contactName: current.ContactName ?? '', contactEmail: current.ContactEmail ?? '' });
    this.editingInformations.set(true);
  }

  cancelEditInformations(): void {
    this.editingInformations.set(false);
  }

  submitInformations = (): Observable<IClient> => {
    const current = this.detail();
    if (this.informationsForm.invalid || !current?.Id) {
      this.informationsForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.error.set(null);
    const raw = this.informationsForm.getRawValue();

    return this.clientService
      .update({ Id: current.Id, Name: raw.name, ContactName: raw.contactName || null, ContactEmail: raw.contactEmail || null })
      .pipe(
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

  // --- Plafonds (édition réservée à l'administration générale)
  startEditLimits(): void {
    const current = this.detail();
    if (!current) return;
    this.limitsForm.reset({
      limitChoirs: current.ChoirLimit,
      limitMembers: current.MemberLimit,
      quotaStorageGo: Math.round((current.StorageQuotaBytes / BYTES_PER_GB) * 100) / 100,
      maxFileSizeMb: Math.round((current.MaxFileSizeBytes / BYTES_PER_MB) * 100) / 100
    });
    this.editingLimits.set(true);
  }

  cancelEditLimits(): void {
    this.editingLimits.set(false);
  }

  submitLimits = (): Observable<IClient> => {
    const current = this.detail();
    if (this.limitsForm.invalid || !current?.Id) {
      this.limitsForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.error.set(null);
    const raw = this.limitsForm.getRawValue();

    return this.clientService
      .updateLimits({
        Id: current.Id,
        ChoirLimit: raw.limitChoirs,
        MemberLimit: raw.limitMembers,
        StorageQuotaBytes: Math.round(raw.quotaStorageGo * BYTES_PER_GB),
        MaxFileSizeBytes: Math.round(raw.maxFileSizeMb * BYTES_PER_MB)
      })
      .pipe(
        tap(updated => {
          this.detail.set(updated);
          this.editingLimits.set(false);
          this.toastService.success('Plafonds mis à jour.');
        }),
        catchError((err: unknown) => {
          if (!(err instanceof Error && err.message === 'validation')) {
            this.error.set("Impossible d'enregistrer les plafonds. Merci de réessayer.");
          }
          return throwError(() => err);
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };

  // --- Statut : suspension (action la plus lourde), archivage, réactivation
  suspendreAction = (): Observable<IClient> => {
    const current = this.detail();
    if (!current?.Id) return throwError(() => new Error('no-detail'));
    this.error.set(null);
    const clientId = current.Id;

    return this.clientService.getImpactSuspension(clientId).pipe(
      switchMap(impact =>
        from(
          this.confirmService.confirm({
            title: 'Suspendre ce client ?',
            message: `Suspendre « ${current.Name} » coupe immédiatement l'accès à toutes ses chorales.`,
            impacts: [`${impact.ChoirCount} chorale(s) concernée(s)`, `${impact.MemberCount} membre(s) concerné(s)`],
            danger: true,
            confirmationKeyword: current.Name,
            confirmationLabel: 'Suspendre'
          })
        )
      ),
      switchMap(confirmed =>
        confirmed
          ? this.clientService.changeStatus({ Id: clientId, Status: ClientStatusEnum.Suspended })
          : throwError(() => new Error('cancelled'))
      ),
      tap(updated => {
        this.detail.set(updated);
        this.toastService.success('Client suspendu.');
      }),
      catchError((err: unknown) => this.handleStatusError(err)),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  archiveAction = (): Observable<IClient> => {
    const current = this.detail();
    if (!current?.Id) return throwError(() => new Error('no-detail'));
    this.error.set(null);
    const clientId = current.Id;

    return from(
      this.confirmService.confirm({
        title: 'Archiver ce client ?',
        message: `Archiver « ${current.Name} » est une action définitive (statut terminal, aucune réactivation possible ensuite).`,
        danger: true,
        confirmationKeyword: current.Name,
        confirmationLabel: 'Archiver'
      })
    ).pipe(
      switchMap(confirmed =>
        confirmed
          ? this.clientService.changeStatus({ Id: clientId, Status: ClientStatusEnum.Archived })
          : throwError(() => new Error('cancelled'))
      ),
      tap(updated => {
        this.detail.set(updated);
        this.toastService.success('Client archivé.');
      }),
      catchError((err: unknown) => this.handleStatusError(err)),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  // 409 plafond dépassé : message chiffré du back affiché tel quel, avec renvoi vers l'onglet
  // Plafonds pour ajuster les limites (pas de nouvelle tentative possible avant ajustement).
  reactivateAction = (): Observable<IClient> => {
    const current = this.detail();
    if (!current?.Id) return throwError(() => new Error('no-detail'));
    this.error.set(null);

    return this.clientService.reactivate(current.Id).pipe(
      tap(updated => {
        this.detail.set(updated);
        this.toastService.success('Client réactivé.');
      }),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 409) {
          const message = (err.error as { Message?: string } | null)?.Message;
          this.error.set(message ?? 'Réactivation impossible.');
        } else {
          this.error.set('Impossible de réactiver ce client. Merci de réessayer.');
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  goToCaps(): void {
    this.activeTab.set('limits');
  }

  // --- Managers
  assignManager = (): Observable<unknown> => {
    const current = this.detail();
    if (this.assignForm.invalid || !current?.Id) {
      this.assignForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.assignError.set(null);
    const email = this.assignForm.getRawValue().email;

    return this.clientService.assignManager(current.Id, { Email: email }).pipe(
      tap(() => {
        this.toastService.success('Responsable désigné.');
        this.assignForm.reset({ email: '' });
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          if (err instanceof HttpErrorResponse && err.status === 404) {
            this.assignError.set('Aucun compte ne correspond à cette adresse e-mail.');
          } else if (err instanceof HttpErrorResponse && err.status === 409) {
            this.assignError.set('Cet utilisateur est déjà responsable de ce client.');
          } else {
            this.assignError.set('Impossible de désigner ce responsable. Merci de réessayer.');
          }
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  removeManager = (): Observable<unknown> => {
    const current = this.detail();
    if (this.removeForm.invalid || !current?.Id) {
      this.removeForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    const userId = this.removeForm.getRawValue().userId;
    // Capturé dans une constante locale : TypeScript ne retient pas le narrowing de
    // `current.Id` (vérifié ci-dessus) à travers la fermeture passée à switchMap.
    const clientId = current.Id;

    return from(
      this.confirmService.confirm({
        title: 'Retirer ce responsable ?',
        message: `L'utilisateur ${userId} ne sera plus responsable de « ${current.Name} ».`,
        danger: true,
        confirmationLabel: 'Retirer'
      })
    ).pipe(
      switchMap(confirmed => {
        if (!confirmed) return throwError(() => new Error('cancelled'));
        this.removeError.set(null);
        return this.clientService.removeManager(clientId, userId);
      }),
      tap(() => {
        this.toastService.success('Responsable retiré.');
        this.removeForm.reset({ userId: '' });
      }),
      catchError((err: unknown) => {
        if (err instanceof Error && err.message === 'cancelled') return throwError(() => err);
        this.removeError.set('Impossible de retirer ce responsable. Vérifiez son identifiant.');
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  onChoirsFilterChange(value: string): void {
    this.choirsFilterText.set(value);
    this.choirsPage.set(1);
    this.loadChoirs();
  }

  onChoirsSortChange(event: { active: string; direction: 'asc' | 'desc' }): void {
    this.choirsSortActive.set(event.active);
    this.choirsSortDirection.set(event.direction);
    this.loadChoirs();
  }

  onChoirsPageChange(page: number): void {
    this.choirsPage.set(page);
    this.loadChoirs();
  }

  private handleStatusError(err: unknown): Observable<never> {
    if (err instanceof Error && err.message === 'cancelled') {
      return throwError(() => err);
    }
    if (err instanceof HttpErrorResponse && (err.status === 409 || err.status === 400)) {
      const message = (err.error as { Message?: string } | null)?.Message;
      this.error.set(message ?? 'Changement de statut impossible.');
    } else {
      this.error.set('Impossible de mettre à jour le statut de ce client. Merci de réessayer.');
    }
    return throwError(() => err);
  }

  private load(): void {
    const clientId = this.id();
    if (!isValidUuid(clientId)) {
      this.loading.set(false);
      this.error.set('Identifiant de client invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.clientService
      .getById(clientId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          this.detail.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger ce client.');
        }
      });
  }

  private loadChoirs(): void {
    const clientId = this.id();
    if (!isValidUuid(clientId)) return;

    this.choirsLoading.set(true);
    this.clientService
      .getChoirs(clientId, {
        Page: this.choirsPage(),
        PageSize: CHOIRS_PAGE_SIZE,
        SortActive: this.choirsSortActive(),
        SortDirection: this.choirsSortDirection(),
        Filter: this.choirsFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.choirsLoaded = true;
          this.choirsItems.set(result.Items);
          this.choirsTotalCount.set(result.TotalCount);
          this.choirsLoading.set(false);
        },
        error: () => {
          this.choirsLoading.set(false);
          this.error.set('Impossible de charger les chorales de ce client.');
        }
      });
  }
}
