import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, tap, throwError } from 'rxjs';
import { ClientService } from '@app/services/admin/client.service';
import { ToastService } from '@app/services/toast.service';
import { RoutePaths } from '@core/route-paths';
import { parseEnumQueryParam, parseGuidListQueryParam, parseTriStateBooleanQueryParam } from '@core/query-params.util';
import { DataTableComponent, DEFAULT_PAGE_SIZE, IDataTableColumn } from '@app/components/shared/data-table/data-table.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IClient } from '@models/admin-models/client.model';
import { ICreateClient } from '@models/admin-models/client-actions.model';
import { ClientStatusEnum, getStatusClientLabel } from '@app/enums/status-client.enum';
import { formatBytes } from '@app/services/admin/format-bytes.util';

// Liste blanche de validation du query param Statut (voir query-params.util.ts) — pas de
// tableau `allStatuss` protégé existant sur ce composant (aucun filtre par statut n'était
// exposé côté UI avant ce raccordement, voir plus bas).
const ALL_CLIENT_STATUSES: ClientStatusEnum[] = [ClientStatusEnum.Active, ClientStatusEnum.Suspended, ClientStatusEnum.Archived];


// Liste des clients (structures). Seule entité de ce lot que l'administration générale crée
// réellement (`10-D23`, décision produit : jamais de chorale ni d'événement créés depuis /admin).
@Component({
  selector: 'app-client-list',
  standalone: true,
  imports: [ModalComponent, DataTableComponent, PageHeaderComponent, ReactiveFormsModule, FormFieldComponent, SubmitOnceDirective, IconComponent],
  templateUrl: './client-list.component.html',
  styleUrl: './client-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientListComponent {
  private readonly clientService = inject(ClientService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getStatusClientLabel = getStatusClientLabel;
  protected readonly ClientStatusEnum = ClientStatusEnum;
  protected readonly formatBytes = formatBytes;

  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: IClient }>>('tplStatus');
  private readonly tplStorage = viewChild<TemplateRef<{ $implicit: IClient }>>('tplStockage');

  readonly columns = computed<IDataTableColumn<IClient>[]>(() => [
    { key: 'Name', label: 'Nom', sortable: true },
    { key: 'Status', label: 'Statut', sortable: true, cellTemplate: this.tplStatus() },
    { key: 'ChoirCount', label: 'Chorales' },
    { key: 'MemberCount', label: 'Membres' },
    { key: 'UsedStorageBytes', label: 'Stockage utilisé', cellTemplate: this.tplStorage() }
  ]);

  readonly items = signal<IClient[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly filterText = signal('');

  // Valeurs initiales lues depuis les query params de la navigation (tuiles "Active" /
  // "Suspended" / "Archivés" du tableau de bord admin, voir dashboard.component.ts) —
  // ActivatedRouteSnapshot, lecture unique au chargement. Absent, malformé ou hors énumération
  // → '' silencieusement (voir query-params.util.ts), jamais d'exception.
  //
  // Écart assumé : Statut/ClientIds/ProcheDuPlafond sont en cours d'ajout côté back au moment
  // de ce raccordement (ClientController.GetPaged n'accepte encore que PaginateViewModel nu,
  // voir client.service.ts) — ils sont donc lus et transmis à l'appel réseau (premier appel
  // déjà filtré dès que le back les acceptera), mais cet écran n'expose aujourd'hui AUCUN
  // contrôle pour les update après coup (aucun sélecteur Statut/ClientIds/ProcheDuPlafond
  // n'existait avant ce correctif, et en ajouter un est une évolution de mise en page hors du
  // périmètre de ce raccordement ciblé). Signalé explicitement plutôt que silencieusement.
  readonly filterStatus = signal<ClientStatusEnum | ''>(
    parseEnumQueryParam(this.route.snapshot.queryParamMap, 'Status', ALL_CLIENT_STATUSES) ?? ''
  );
  readonly filterClientIds = signal<string[]>(parseGuidListQueryParam(this.route.snapshot.queryParamMap, 'ClientIds') ?? []);
  readonly filterNearCap = signal(parseTriStateBooleanQueryParam(this.route.snapshot.queryParamMap, 'ProcheDuPlafond'));

  readonly showCreateForm = signal(false);
  readonly createError = signal<string | null>(null);

  readonly createForm = this.fb.nonNullable.group({
    name: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(150)]),
    contactName: this.fb.nonNullable.control('', [Validators.maxLength(150)]),
    contactEmail: this.fb.nonNullable.control('', [Validators.email, Validators.maxLength(256)])
  });

  constructor() {
    this.load();
  }

  onFilterChange(value: string): void {
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

  // Revient à la première page : rester sur la page 7 après être passé de 25 à 100 lignes
  // afficherait un écart de données que rien ne signale à l'écran.
  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  onRowClick(row: IClient): void {
    if (!row.Id) return;
    this.router.navigate(['/', RoutePaths.Admin, RoutePaths.AdminClients, row.Id]);
  }

  openCreateForm(): void {
    this.createError.set(null);
    this.createForm.reset({ name: '', contactName: '', contactEmail: '' });
    this.showCreateForm.set(true);
  }

  cancelCreateForm(): void {
    this.showCreateForm.set(false);
  }

  submitCreate = (): Observable<IClient> => {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.createError.set(null);
    const raw = this.createForm.getRawValue();
    const payload: ICreateClient = {
      Name: raw.name,
      ContactName: raw.contactName || null,
      ContactEmail: raw.contactEmail || null
    };

    return this.clientService.create(payload).pipe(
      tap(() => {
        this.toastService.success('Client créé.');
        this.showCreateForm.set(false);
        this.load();
      }),
      catchError((err: unknown) => {
        if (!(err instanceof Error && err.message === 'validation')) {
          this.createError.set(
            err instanceof HttpErrorResponse && err.status === 400
              ? 'Le nom du client est requis.'
              : 'Impossible de créer ce client. Merci de réessayer.'
          );
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const status = this.filterStatus();
    const procheDuCap = this.filterNearCap();

    this.clientService
      .getPaged(
        {
          Page: this.page(),
          PageSize: this.pageSize(),
          SortActive: this.sortActive(),
          SortDirection: this.sortDirection(),
          Filter: this.filterText() || undefined
        },
        {
          Status: status === '' ? undefined : status,
          ClientIds: this.filterClientIds().length > 0 ? this.filterClientIds() : undefined,
          NearCap: procheDuCap === '' ? undefined : procheDuCap === 'true'
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
          this.error.set('Impossible de charger les clients. Merci de réessayer.');
        }
      });
  }
}
