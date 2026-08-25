import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MembershipRequestManagementService } from '@app/services/onboarding/membership-request-management.service';
import { JoinCodePanelComponent } from '@app/components/onboarding/join-code-panel/join-code-panel.component';
import { ApproveRequestModalComponent } from '@app/components/members/approve-request-modal/approve-request-modal.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { ConfirmService } from '@app/services/confirm.service';
import { AuthStore } from '@core/auth.store';
import { IMembershipRequestListItem } from '@models/onboarding-models/membership-request-list-item.model';
import { IApproveRequestRequest } from '@models/onboarding-models/approve-request-request.model';
import { MembershipRequestStatusEnum, getStatusMembershipRequestLabel } from '@app/enums/status-membership-request.enum';

const PAGE_SIZE = 50;

// Segment "Demandes" de /management/:spaceId/membres. La route Membres est déjà restreinte au
// rôle Responsable côté app.routes.ts (spaceRoleGuard([Responsable], [Chorale])) — ce
// composant n'est donc jamais rendu pour un SectionLeader, qui ne peut pas atteindre la route du
// tout ("segment absent, pas grisé" est déjà garanti par le guard existant, pas par une
// condition de template ici).
@Component({
  selector: 'app-membership-requests-list',
  standalone: true,
  imports: [DataStateComponent, JoinCodePanelComponent, ApproveRequestModalComponent],
  templateUrl: './membership-requests-list.component.html',
  styleUrl: './membership-requests-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MembershipRequestsListComponent {
  private readonly membershipRequestManagementService = inject(MembershipRequestManagementService);
  private readonly confirmService = inject(ConfirmService);
  protected readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly getStatusMembershipRequestLabel = getStatusMembershipRequestLabel;
  protected readonly MembershipRequestStatusEnum = MembershipRequestStatusEnum;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly items = signal<IMembershipRequestListItem[]>([]);
  readonly capAtteint = signal(false);
  readonly admettingRequest = signal<IMembershipRequestListItem | null>(null);
  readonly isSubmittingApproval = signal(false);
  readonly refusingRequestId = signal<string | null>(null);
  // Panneau replié par défaut : n'instancie JoinCodePanelComponent (et son appel HTTP
  // GET JoinCode) qu'à l'ouverture explicite, pas au montage de la liste.
  readonly showCodePanel = signal(false);

  // En attente d'abord, puis les demandes déjà traitées (historique) — le contrat GetPaged ne
  // propose aucun filtre serveur par Statut (voir MembershipRequestManagementService).
  readonly itemsTries = computed(() =>
    [...this.items()].sort((a, b) => {
      const aIsPending = a.Status === MembershipRequestStatusEnum.Pending ? 0 : 1;
      const bIsPending = b.Status === MembershipRequestStatusEnum.Pending ? 0 : 1;
      return aIsPending - bIsPending;
    })
  );

  constructor() {
    this.load();
  }

  ancienneteLabel(createdAt: string): string {
    const created = new Date(createdAt);
    if (Number.isNaN(created.getTime())) return '';
    const jours = Math.max(0, Math.floor((Date.now() - created.getTime()) / (1000 * 60 * 60 * 24)));
    if (jours === 0) return "Aujourd'hui";
    if (jours === 1) return 'Il y a 1 jour';
    return `Il y a ${jours} jours`;
  }

  toggleCodePanel(): void {
    this.showCodePanel.update(v => !v);
  }

  openApproval(request: IMembershipRequestListItem): void {
    this.admettingRequest.set(request);
  }

  closeApproval(): void {
    this.admettingRequest.set(null);
  }

  confirmApproval(payload: IApproveRequestRequest): void {
    const request = this.admettingRequest();
    const spaceId = this.authStore.activeSpaceId();
    if (!request || !spaceId) return;

    this.isSubmittingApproval.set(true);

    this.membershipRequestManagementService
      .approve(spaceId, request.Id, payload)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.isSubmittingApproval.set(false);
          this.admettingRequest.set(null);
          this.replaceItem(updated);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmittingApproval.set(false);
          this.admettingRequest.set(null);
          if (err.status === 409) {
            this.capAtteint.set(true);
          }
        }
      });
  }

  async decline(request: IMembershipRequestListItem): Promise<void> {
    const spaceId = this.authStore.activeSpaceId();
    if (!spaceId) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Refuser cette demande ?',
      message: `${request.UserFullName} ne pourra pas rejoindre la chorale via cette demande.`,
      confirmationLabel: 'Refuser',
      danger: true
    });
    if (!confirmed) return;

    this.refusingRequestId.set(request.Id);

    this.membershipRequestManagementService
      .decline(spaceId, request.Id, {})
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: updated => {
          this.refusingRequestId.set(null);
          this.replaceItem(updated);
        },
        error: () => this.refusingRequestId.set(null)
      });
  }

  private replaceItem(updated: IMembershipRequestListItem): void {
    this.items.update(list => list.map(item => (item.Id === updated.Id ? updated : item)));
  }

  private load(): void {
    const spaceId = this.authStore.activeSpaceId();
    if (!spaceId) {
      this.loading.set(false);
      this.error.set('Aucune chorale actif sélectionnée.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.membershipRequestManagementService
      .getPaged(spaceId, { Page: 1, PageSize: PAGE_SIZE })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.loading.set(false);
          this.items.set(result.Items);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les demandes. Merci de réessayer.');
        }
      });
  }
}
