import { ActivatedRoute, RouterLink } from '@angular/router';
import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { ChoirService } from '@app/services/client/choir.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { RoutePaths, managementPath } from '@core/route-paths';
import { isValidUuid } from '@core/uuid.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { DataTableComponent, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { IBreadcrumbItem, PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IChoirDetail } from '@models/client-models/choir-detail.model';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { ChoirStatusEnum, getStatusChoirLabel, getStatusChoirTransitionsAllowed } from '@app/enums/status-choir.enum';
import { MemberStatusEnum, getMemberStatusLabel } from '@app/enums/member-status.enum';
import { UserRoleEnum, getUserRolesLabel, userRoleFromString } from '@app/enums/user-role.enum';

const CHOIR_MASTERS_PAGE_SIZE = 10;

// Fiche chorale de la zone « Ma structure » (/client/:clientId/choirs/:choirId, policy
// ClientManager) — `Spec/chorale/13-ecrans-ma-structure.md` § Fiche chorale. Pas d'onglets
// Membres/Chants/Événements (hors périmètre explicite de cette spec, contrairement à la fiche
// équivalente de l'administration générale) : seuls les indicateurs agrégés, le cycle de vie et
// les chefs de chœur sont présentés ici.
//
// clientId n'est PAS porté par un input() lié via withComponentInputBinding() : cette route a un
// chemin ENFANT NON vide ('choirs/:choirId') sous /client/:clientId, et paramsInheritanceStrategy
// reste au défaut Angular 'emptyOnly' (voir app.config.ts) — un ancêtre à chemin non vide ne
// fusionne pas ses params dans le paramMap du descendant. On remonte donc explicitement la chaîne
// d'ActivatedRoute (même pattern que findSpaceId dans space-role.guard.ts) plutôt que d'activer
// 'always' globalement, ce qui aurait un effet de bord sur tout le reste du routage.
@Component({
  selector: 'app-choir-detail',
  standalone: true,
  imports: [
    RouterLink,
    ReactiveFormsModule,
    DataStateComponent,
    DataTableComponent,
    PageHeaderComponent,
    FormFieldComponent,
    SubmitOnceDirective,
    IconComponent
  ],
  templateUrl: './choir-detail.component.html',
  styleUrl: './choir-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChoirDetailComponent {
  private readonly choirService = inject(ChoirService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly authStore = inject(AuthStore);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly choirId = input<string | undefined>(undefined);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly ChoirStatusEnum = ChoirStatusEnum;
  protected readonly getStatusChoirLabel = getStatusChoirLabel;
  protected readonly MemberStatusEnum = MemberStatusEnum;
  protected readonly getMemberStatusLabel = getMemberStatusLabel;
  protected readonly getUserRolesLabel = getUserRolesLabel;

  readonly detail = signal<IChoirDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  protected readonly breadcrumb = computed<IBreadcrumbItem[]>(() => [
    { label: 'Ma structure', link: '../..' },
    { label: this.detail()?.Name ?? 'Chorale' }
  ]);

  readonly transitionsAllowed = computed(() => {
    const current = this.detail();
    return current ? getStatusChoirTransitionsAllowed(current.Status) : [];
  });

  // Visible seulement si l'appelant est chef de chœur (rôle Manager) de CET espace précis —
  // vérification d'affichage uniquement (UX), jamais une autorisation : le back reste seul
  // décisionnaire (policy scopée par X-Space-Id sur les routes /management/:spaceId).
  protected readonly canOpenManagement = computed(() => {
    const id = this.choirId();
    if (!isValidUuid(id)) return false;
    const assignment = this.authStore.spaceRoles().find(space => space.SpaceId === id);
    if (!assignment) return false;
    return assignment.Roles.some(role => userRoleFromString(role) === UserRoleEnum.Manager);
  });

  protected readonly managementLink = computed(() => managementPath(this.choirId() ?? '', RoutePaths.Dashboard));

  // --- Chefs de chœur (désignation + retrait) — repris tel quel de l'ancien
  // ChoirMasterListComponent, aucune régression fonctionnelle.
  private readonly tplRoles = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplRoles');
  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplStatus');
  private readonly tplActions = viewChild<TemplateRef<{ $implicit: IMemberChoir }>>('tplActions');

  readonly choirMastersColumns = computed<IDataTableColumn<IMemberChoir>[]>(() => [
    { key: 'UserFullName', label: 'Nom' },
    { key: 'UserEmail', label: 'Email' },
    { key: 'Roles', label: 'Rôle(s)', cellTemplate: this.tplRoles() },
    { key: 'Status', label: 'Statut', cellTemplate: this.tplStatus() },
    { key: 'Actions', label: '', cellTemplate: this.tplActions() }
  ]);

  readonly choirMastersItems = signal<IMemberChoir[]>([]);
  readonly choirMastersTotalCount = signal(0);
  readonly choirMastersLoading = signal(false);
  readonly choirMastersPage = signal(1);
  readonly choirMastersFilterText = signal('');

  readonly assignForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)])
  });
  readonly assignError = signal<string | null>(null);

  constructor() {
    // choirId est un signal input lié via withComponentInputBinding() : sa valeur n'est posée
    // par le Router qu'APRÈS la construction du composant (voir MyStructureComponent /
    // ChoirDetailComponent admin) — effect() re-déclenche les chargements dès qu'elle prend sa
    // valeur réelle ; les deux méthodes gèrent en interne l'id absent/invalide.
    effect(() => {
      this.loadDetail();
      this.loadChoirMasters();
    });
  }

  onChoirMastersFilterChange(value: string): void {
    this.choirMastersFilterText.set(value);
    this.choirMastersPage.set(1);
    this.loadChoirMasters();
  }

  onChoirMastersPageChange(page: number): void {
    this.choirMastersPage.set(page);
    this.loadChoirMasters();
  }

  // Une fonction par transition cible, liée dynamiquement via [appSubmitOnce]="statusAction(target)"
  // dans le gabarit — seules les transitions de transitionsAllowed() sont proposées.
  statusAction(target: ChoirStatusEnum): () => Observable<IChoirDetail> {
    return () => this.changeStatusTo(target);
  }

  assignChoirMaster = (): Observable<unknown> => {
    const id = this.choirId();
    if (this.assignForm.invalid || !isValidUuid(id)) {
      this.assignForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.assignError.set(null);
    const email = this.assignForm.getRawValue().email;

    return this.choirService.assignChoirMaster(id, { Email: email }).pipe(
      tap(() => {
        this.toastService.success('Chef de chœur désigné.');
        this.assignForm.reset({ email: '' });
        this.loadChoirMasters();
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          this.assignError.set(this.extractErrorMessage(err, this.defaultAssignErrorMessage(err)));
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  removeChoirMaster(member: IMemberChoir): void {
    const id = this.choirId();
    if (!isValidUuid(id)) return;

    from(
      this.confirmService.confirm({
        title: 'Retirer ce chef de chœur ?',
        message: `${member.UserFullName ?? member.UserEmail ?? 'Cet utilisateur'} ne sera plus chef de chœur de cette chorale.`,
        danger: true,
        confirmationLabel: 'Retirer'
      })
    )
      .pipe(
        switchMap(confirmed => {
          if (!confirmed) return throwError(() => new Error('cancelled'));
          this.error.set(null);
          return this.choirService.removeChoirMaster(id, member.UserId);
        }),
        tap(() => {
          this.toastService.success('Chef de chœur retiré.');
          this.loadChoirMasters();
        }),
        catchError((err: unknown) => {
          if (!(err instanceof Error && err.message === 'cancelled')) {
            this.error.set(this.extractErrorMessage(err, this.defaultRemoveErrorMessage(err)));
          }
          return throwError(() => err);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private changeStatusTo(target: ChoirStatusEnum): Observable<IChoirDetail> {
    const current = this.detail();
    const clientId = this.findClientId();
    if (!current || !isValidUuid(clientId)) return throwError(() => new Error('no-detail'));
    this.error.set(null);

    if (target === ChoirStatusEnum.Archived) {
      // Impacts déjà en mémoire (MemberCount/SongCount/UpcomingEventCount de la fiche) — pas de
      // second appel HTTP dédié, contrairement à la fiche admin (getImpactArchivage) qui n'a pas
      // ces compteurs déjà chargés à ce stade.
      return from(
        this.confirmService.confirm({
          title: 'Archiver cette chorale ?',
          message: `La chorale « ${current.Name} » sera archivée : son contenu est conservé mais devient invisible des membres.`,
          impacts: [
            `${current.MemberCount} membre(s)`,
            `${current.SongCount} chant(s)`,
            `${current.UpcomingEventCount} événement(s) à venir`
          ],
          danger: true,
          confirmationLabel: 'Archiver'
        })
      ).pipe(
        switchMap(confirmed =>
          confirmed ? this.choirService.changeStatus(clientId, current.Id, target) : throwError(() => new Error('cancelled'))
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
        confirmed ? this.choirService.changeStatus(clientId, current.Id, target) : throwError(() => new Error('cancelled'))
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

  // Lit le message métier renvoyé par le back (ApiClientErrorResponse.Message, voir
  // ApiErrorInterceptor) quand il est présent — un même code 409 recouvre plusieurs causes
  // distinctes ici (dernier chef de chœur / chorale non modifiable), le back est la seule
  // source fiable du libellé exact.
  private extractErrorMessage(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      const message = (err.error as { Message?: string } | null)?.Message;
      if (message) return message;
    }
    return fallback;
  }

  private defaultAssignErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 404) {
      return 'Aucun compte ne correspond à cette adresse e-mail. Le chef de chœur doit déjà avoir un compte.';
    }
    if (err instanceof HttpErrorResponse && err.status === 409) {
      return 'Impossible de désigner ce chef de chœur : plafond atteint ou chorale non modifiable actuellement.';
    }
    return 'Impossible de désigner ce chef de chœur. Merci de réessayer.';
  }

  private defaultRemoveErrorMessage(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.status === 400) {
      return 'Retirez d’abord le rôle de chef de pupitre avant de retirer ce chef de chœur.';
    }
    if (err instanceof HttpErrorResponse && err.status === 409) {
      return 'Désignez un remplaçant avant de retirer ce chef de chœur, ou la chorale n’accepte plus de modification.';
    }
    return 'Impossible de retirer ce chef de chœur. Merci de réessayer.';
  }

  // Remonte la chaîne d'ActivatedRoute pour trouver le clientId porté par l'ancêtre
  // /client/:clientId — voir le commentaire d'en-tête de fichier pour la raison (non hérité par
  // withComponentInputBinding sur ce chemin enfant non vide).
  private findClientId(): string | null {
    for (let current: ActivatedRoute | null = this.route; current; current = current.parent) {
      const value = current.snapshot.paramMap.get('clientId');
      if (value !== null) return value;
    }
    return null;
  }

  private loadDetail(): void {
    const choirId = this.choirId();
    const clientId = this.findClientId();
    if (!isValidUuid(choirId) || !isValidUuid(clientId)) {
      this.loading.set(false);
      this.error.set('Identifiant de chorale invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.choirService
      .getDetail(clientId, choirId)
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

  private loadChoirMasters(): void {
    const id = this.choirId();
    if (!isValidUuid(id)) return;

    this.choirMastersLoading.set(true);
    this.choirService
      .getChoirMasters(id, {
        Page: this.choirMastersPage(),
        PageSize: CHOIR_MASTERS_PAGE_SIZE,
        Filter: this.choirMastersFilterText() || undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.choirMastersItems.set(result.Items);
          this.choirMastersTotalCount.set(result.TotalCount);
          this.choirMastersLoading.set(false);
        },
        error: () => {
          this.choirMastersLoading.set(false);
          this.error.set('Impossible de charger les chefs de chœur de cette chorale.');
        }
      });
  }
}
