import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, tap, throwError } from 'rxjs';
import { ChoirMembersService } from '@app/services/members/choir-members.service';
import { MembershipRequestManagementService } from '@app/services/onboarding/membership-request-management.service';
import { ToastService } from '@app/services/toast.service';
import { AuthStore } from '@core/auth.store';
import { debounce } from '@core/debounce.util';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { MembershipRequestsListComponent } from '@app/components/members/membership-requests-list/membership-requests-list.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IMemberChoir } from '@models/members-models/member-choir.model';
import { MemberStatusEnum, getMemberStatusLabel } from '@app/enums/member-status.enum';
import { UserRoleEnum, getUserRolesLabel } from '@app/enums/user-role.enum';
import { ALL_VOICE_PARTS, VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';

const DEFAULT_PAGE_SIZE = 10;
const FILTER_DEBOUNCE_MS = 300;

// Liste paginée des membres de la chorale actif, avec leur pupitre, et invitation par email
// (Update/ChangeRole/ChangeStatus restent hors périmètre). Route déjà protégée par
// spaceRoleGuard([Responsable], [Chorale]) (app.routes.ts) — n'est jamais reachable que sous
// /management/:spaceId avec spaceId = l'id de la chorale (SpaceId EST le ChoirId pour un
// espace de type Chorale).
@Component({
  selector: 'app-member-list',
  standalone: true,
  imports: [PaginationComponent, 
    ReactiveFormsModule,
    DataStateComponent,
    FormFieldComponent,
    IconComponent,
    SubmitOnceDirective,
    MembershipRequestsListComponent
  ],
  templateUrl: './member-list.component.html',
  styleUrl: './member-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MemberListComponent {
  private readonly choirMembersService = inject(ChoirMembersService);
  private readonly membershipRequestManagementService = inject(MembershipRequestManagementService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getMemberStatusLabel = getMemberStatusLabel;
  protected readonly getUserRolesLabel = getUserRolesLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allVoiceParts = ALL_VOICE_PARTS;
  protected readonly MemberStatusEnum = MemberStatusEnum;

  // Segment "Demandes" — pas d'entrée de sidebar dédiée (badge numérique porté par le lien
  // Membres, voir sidebar.component). Onglet structurellement absent du DOM (pas seulement
  // masqué en CSS) pour un SectionLeader : la route /membres est déjà réservée au rôle
  // Responsable côté app.routes.ts (spaceRoleGuard), cette vérification défensive garde le
  // même comportement si ce guard venait à être élargi un jour.
  readonly activeTab = signal<'members' | 'demandes'>('members');
  // Même garde pour le segment Demandes et pour l'invitation : les deux relèvent du seul
  // Responsable (`02` § Matrice). Un unique computed, jamais deux règles à maintenir.
  protected readonly isManager = computed(
    () => this.authStore.isGlobalAdmin() || this.authStore.activeSpaceRoles().includes(UserRoleEnum.Manager)
  );
  readonly pendingRequestsCount = signal(0);

  readonly items = signal<IMemberChoir[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly filterText = signal('');

  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  readonly showInviteForm = signal(false);
  // primaryVoicePart est requis ici alors que le back l'accepte nul : une invitation sans
  // pupitre produit un membre qu'aucune consigne ni aucun enregistrement par voix n'atteint,
  // et que seul un passage par l'écran d'admission peut rattraper.
  readonly inviteForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    firstname: this.fb.nonNullable.control(''),
    lastname: this.fb.nonNullable.control(''),
    primaryVoicePart: this.fb.nonNullable.control<VoicePartEnum | null>(null, [Validators.required])
  });

  // Anti-rebond sur la saisie du filtre texte (300 ms) — évite un appel HTTP par frappe.
  private readonly debouncedLoad = debounce(() => this.load(), FILTER_DEBOUNCE_MS);

  constructor() {
    this.load();
    if (this.isManager()) {
      this.loadRequestsCount();
    }
  }

  toggleInviteForm(): void {
    this.showInviteForm.update(open => !open);
    if (!this.showInviteForm()) {
      this.inviteForm.reset({ email: '', firstname: '', lastname: '', primaryVoicePart: null });
    }
  }

  // Bouton sous appSubmitOnce : rejet synchrone si le formulaire est invalide (jamais d'appel
  // HTTP dans ce cas). Le panneau se referme au succès — la directive reste désactivée après
  // un succès, le laisser ouvert interdirait une seconde invitation.
  // Aucun toast d'erreur ici : ApiErrorInterceptor porte déjà les 409 (déjà membre) et les
  // dépassements de plafond de membres du client.
  submitInvite = (): Observable<IMemberChoir> => {
    const choirId = this.authStore.activeSpaceId();
    if (this.inviteForm.invalid || !choirId) {
      this.inviteForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    const { email, firstname, lastname, primaryVoicePart } = this.inviteForm.getRawValue();
    // Validators.required a déjà écarté le cas null ci-dessus — ce repli n'existe que pour
    // satisfaire le typage `VoicePartEnum | null` du contrôle.
    if (primaryVoicePart === null) {
      this.inviteForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    return this.choirMembersService
      .invite({
        ChoirId: choirId,
        Email: email,
        Firstname: firstname.trim() || undefined,
        Lastname: lastname.trim() || undefined,
        PrimaryVoicePart: primaryVoicePart
      })
      .pipe(
        tap(() => {
          this.toast.success(`Invitation envoyée à ${email}.`);
          this.inviteForm.reset({ email: '', firstname: '', lastname: '', primaryVoicePart: null });
          this.showInviteForm.set(false);
          this.load();
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };

  selectTab(tab: 'members' | 'demandes'): void {
    this.showInviteForm.set(false);
    this.activeTab.set(tab);
    if (tab === 'demandes') {
      // Reflète immédiatement une éventuelle admission/refus effectué dans l'onglet.
      this.loadRequestsCount();
    }
  }

  private loadRequestsCount(): void {
    const spaceId = this.authStore.activeSpaceId();
    if (!spaceId) return;

    this.membershipRequestManagementService
      .getPendingCount(spaceId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: count => this.pendingRequestsCount.set(count),
        error: () => {
          // Échec silencieux : le badge reste simplement à sa dernière valeur connue, ce
          // n'est pas une donnée bloquante pour la page Membres elle-même.
        }
      });
  }

  onFilterTextChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.debouncedLoad();
  }

  onSort(field: string): void {
    if (this.sortActive() === field) {
      this.sortDirection.set(this.sortDirection() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortActive.set(field);
      this.sortDirection.set('asc');
    }
    this.load();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    this.load();
  }

  private load(): void {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) {
      this.error.set('Aucune chorale active sélectionnée.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.choirMembersService
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
          this.error.set('Impossible de charger les membres. Merci de réessayer.');
        }
      });
  }
}
