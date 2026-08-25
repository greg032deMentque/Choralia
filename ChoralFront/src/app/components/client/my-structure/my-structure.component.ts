import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { ClientService } from '@app/services/admin/client.service';
import { ChoirService } from '@app/services/client/choir.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { RoutePaths } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { DataTableComponent, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IClient } from '@models/admin-models/client.model';
import { IClientChoirListItem } from '@models/admin-models/client-choir-list-item.model';
import { IClientManagerListItem } from '@models/admin-models/client-manager-list-item.model';
import { ICreateChoir } from '@models/client-models/choir.model';
import { ClientStatusEnum, getStatusClientLabel } from '@app/enums/status-client.enum';
import { formatBytes, isUsageCritical, percentageUsage } from '@app/services/admin/format-bytes.util';

const CHOIRS_PAGE_SIZE = 10;
const MANAGERS_PAGE_SIZE = 10;

export type MyStructureTab = 'choirs' | 'limits' | 'managers';

// Zone « Ma structure » (/client/:clientId, policy ClientManager) — jamais le mot
// « client » dans un texte visible ici (`08` § Ma structure, critère de recette) : on dit
// toujours « structure ». Réutilise DataTableComponent, ConfirmService, FormFieldComponent et
// les mêmes conventions que la zone admin (aucune duplication de gabarit).
//
// clientId est lié via withComponentInputBinding() : cette route est l'enfant à chemin VIDE de
// `/client/:clientId` (paramsInheritanceStrategy par défaut — 'emptyOnly' — hérite donc du
// paramètre du parent), validé (UUID) avant tout appel HTTP (OWASP A01) même si clientRoleGuard
// l'a déjà fait — défense en profondeur, ce composant ne doit pas supposer que le guard a
// toujours raison.
//
// Le ResponsableClient ne modifie ni les informations ni les plafonds (Update/ModifierLimites
// restent Admin-only côté back) — les écritures disponibles ici sont : création d'une chorale
// (ChoirService.create, policy AdminOrClientManager) et désignation/retrait de responsables de
// la structure (ClientService.assignManager/removeManager, policy ClientManager).
@Component({
  selector: 'app-my-structure',
  standalone: true,
  imports: [ModalComponent, 
    ReactiveFormsModule,
    DatePipe,
    DataStateComponent,
    DataTableComponent,
    PageHeaderComponent,
    FormFieldComponent,
    SubmitOnceDirective
  ],
  templateUrl: './my-structure.component.html',
  styleUrl: './my-structure.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MyStructureComponent {
  private readonly clientService = inject(ClientService);
  private readonly choirService = inject(ChoirService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly clientId = input<string | undefined>(undefined);

  protected readonly ClientStatusEnum = ClientStatusEnum;
  protected readonly getStatusClientLabel = getStatusClientLabel;
  protected readonly formatBytes = formatBytes;
  protected readonly isUsageCritical = isUsageCritical;
  protected readonly percentageUsage = percentageUsage;

  readonly structure = signal<IClient | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly activeTab = signal<MyStructureTab>('choirs');

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

  readonly showCreateChoirForm = signal(false);
  readonly createChoirError = signal<string | null>(null);
  // true uniquement sur un 409 de création (plafond ou état de structure) — condition
  // d'affichage du lien vers l'onglet Plafonds dans le gabarit.
  readonly createChoirIsCapError = signal(false);
  readonly createChoirForm = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    description: this.fb.nonNullable.control('', [Validators.maxLength(500)]),
    choirMasterEmail: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)])
  });

  // Désignation par email — inchangée (fonctionne déjà). Le retrait, lui, passe désormais par
  // une action par ligne de managersColumns ci-dessous : plus de champ userId à saisir à la
  // main (voir suppression de removeForm/removeError).
  readonly assignForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)])
  });
  readonly assignError = signal<string | null>(null);

  private readonly tplManagerName = viewChild<TemplateRef<{ $implicit: IClientManagerListItem }>>('tplManagerName');
  private readonly tplManagerRole = viewChild<TemplateRef<{ $implicit: IClientManagerListItem }>>('tplManagerRole');
  private readonly tplManagerDate = viewChild<TemplateRef<{ $implicit: IClientManagerListItem }>>('tplManagerDate');
  private readonly tplManagerActions = viewChild<TemplateRef<{ $implicit: IClientManagerListItem }>>('tplManagerActions');
  readonly managersColumns = computed<IDataTableColumn<IClientManagerListItem>[]>(() => [
    { key: 'Firstname', label: 'Nom', cellTemplate: this.tplManagerName() },
    { key: 'Email', label: 'Email' },
    { key: 'Role', label: 'Rôle', cellTemplate: this.tplManagerRole() },
    { key: 'AssignmentDate', label: 'Depuis le', cellTemplate: this.tplManagerDate() },
    { key: 'Actions', label: '', cellTemplate: this.tplManagerActions() }
  ]);
  readonly managersItems = signal<IClientManagerListItem[]>([]);
  readonly managersTotalCount = signal(0);
  readonly managersLoading = signal(false);
  readonly managersPage = signal(1);
  readonly managersFilterText = signal('');
  private managersLoaded = false;

  constructor() {
    effect(() => {
      this.load();
    });
  }

  selectTab(tab: MyStructureTab): void {
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);
    if (tab === 'choirs' && !this.choirsLoaded) this.loadChoirs();
    if (tab === 'managers' && !this.managersLoaded) this.loadManagers();
  }

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

  onChoirRowClick(row: IClientChoirListItem): void {
    this.router.navigate(['/', RoutePaths.Client, this.clientId(), RoutePaths.ClientChoirs, row.Id]);
  }

  onManagersPageChange(page: number): void {
    this.managersPage.set(page);
    this.loadManagers();
  }

  onManagersFilterChange(value: string): void {
    this.managersFilterText.set(value);
    this.managersPage.set(1);
    this.loadManagers();
  }

  openCreateChoirForm(): void {
    this.createChoirError.set(null);
    this.createChoirIsCapError.set(false);
    this.createChoirForm.reset({ name: '', description: '', choirMasterEmail: '' });
    this.showCreateChoirForm.set(true);
  }

  cancelCreateChoirForm(): void {
    this.showCreateChoirForm.set(false);
  }

  // Sur 409 (plafond ou état de structure), renvoie vers l'onglet Plafonds plutôt qu'un simple
  // message — c'est le seul endroit de l'écran où l'utilisateur peut vérifier la consommation
  // réelle de sa structure.
  goToPlafondsFromCreateError(): void {
    this.showCreateChoirForm.set(false);
    this.selectTab('limits');
  }

  submitCreateChoir = (): Observable<unknown> => {
    const current = this.structure();
    if (this.createChoirForm.invalid || !current?.Id) {
      this.createChoirForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.createChoirError.set(null);
    const raw = this.createChoirForm.getRawValue();
    const payload: ICreateChoir = {
      ClientId: current.Id,
      Name: raw.name,
      Description: raw.description || null,
      ChoirMasterEmail: raw.choirMasterEmail
    };

    return this.choirService.create(payload).pipe(
      tap(() => {
        this.toastService.success('Chorale créée.');
        this.showCreateChoirForm.set(false);
        this.loadChoirs();
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          this.createChoirIsCapError.set(err instanceof HttpErrorResponse && err.status === 409);
          this.createChoirError.set(this.extractCreateErrorMessage(err));
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  private extractCreateErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const backMessage = (err.error as { Message?: string } | null)?.Message;
      if (err.status === 404) {
        return 'Aucun compte ne correspond à cette adresse. Le chef de chœur doit déjà avoir un compte.';
      }
      if (err.status === 409) {
        return backMessage ?? 'Plafond de chorales ou de membres atteint pour votre structure.';
      }
      if (err.status === 400 || err.status === 403) {
        return backMessage ?? 'Impossible de créer cette chorale : vérifiez les champs saisis.';
      }
    }
    return 'Impossible de créer cette chorale. Merci de réessayer.';
  }

  assignManager = (): Observable<unknown> => {
    const current = this.structure();
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
        this.managersLoaded = false;
        this.loadManagers();
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          if (err instanceof HttpErrorResponse && err.status === 404) {
            this.assignError.set('Aucun compte ne correspond à cette adresse e-mail.');
          } else if (err instanceof HttpErrorResponse && err.status === 409) {
            this.assignError.set('Cet utilisateur est déjà responsable de cette structure.');
          } else {
            this.assignError.set('Impossible de désigner ce responsable. Merci de réessayer.');
          }
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  // Retrait par ligne du tableau des responsables — utilise directement UserId de la ligne
  // cliquée, plus jamais un identifiant saisi à la main (ancien removeForm, retiré).
  removeManager(manager: IClientManagerListItem): void {
    const current = this.structure();
    if (!current?.Id) return;
    const structureId = current.Id;

    from(
      this.confirmService.confirm({
        title: 'Retirer ce responsable ?',
        message: `${manager.Firstname} ${manager.Lastname} ne sera plus responsable de cette structure.`,
        danger: true,
        confirmationLabel: 'Retirer'
      })
    )
      .pipe(
        switchMap(confirmed => {
          if (!confirmed) return throwError(() => new Error('cancelled'));
          this.error.set(null);
          return this.clientService.removeManager(structureId, manager.UserId);
        }),
        tap(() => {
          this.toastService.success('Responsable retiré.');
          this.managersLoaded = false;
          this.loadManagers();
        }),
        catchError((err: unknown) => {
          if (!(err instanceof Error && err.message === 'cancelled')) {
            this.error.set('Impossible de retirer ce responsable. Merci de réessayer.');
          }
          return throwError(() => err);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private load(): void {
    const id = this.clientId();
    if (!isValidUuid(id)) {
      this.loading.set(false);
      this.error.set('Identifiant de structure invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.clientService
      .getById(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: structure => {
          this.structure.set(structure);
          this.loading.set(false);
          this.loadChoirs();
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cette structure.');
        }
      });
  }

  private loadChoirs(): void {
    const id = this.clientId();
    if (!isValidUuid(id)) return;

    this.choirsLoading.set(true);
    this.clientService
      .getChoirs(id, {
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
          this.error.set('Impossible de charger les chorales de cette structure.');
        }
      });
  }

  private loadManagers(): void {
    const id = this.clientId();
    if (!isValidUuid(id)) return;

    this.managersLoading.set(true);
    this.clientService
      .getManagers(id, {
        Page: this.managersPage(),
        PageSize: MANAGERS_PAGE_SIZE,
        Filter: this.managersFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.managersLoaded = true;
          this.managersItems.set(result.Items);
          this.managersTotalCount.set(result.TotalCount);
          this.managersLoading.set(false);
        },
        error: () => {
          this.managersLoading.set(false);
          this.error.set('Impossible de charger les responsables de cette structure.');
        }
      });
  }
}
