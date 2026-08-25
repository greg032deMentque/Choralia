import { ChangeDetectionStrategy, Component, DestroyRef, TemplateRef, computed, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { map } from 'rxjs';
import { AdminUserService } from '@app/services/admin/admin-user.service';
import { AdminChoirService } from '@app/services/admin/admin-choir.service';
import { AdminEventService } from '@app/services/admin/admin-event.service';
import { debounce } from '@core/debounce.util';
import { RoutePaths } from '@core/route-paths';
import { parseTriStateBooleanQueryParam } from '@core/query-params.util';
import {
  DataTableComponent,
  DEFAULT_PAGE_SIZE,
  DataTableGroupByFn,
  IDataTableChip,
  IDataTableColumn
} from '@app/components/shared/data-table/data-table.component';
import { EntityMultiSelectModalComponent, EntitySearchFn } from '@app/components/shared/entity-multi-select-modal/entity-multi-select-modal.component';
import { PageHeaderComponent } from '@app/components/shared/page-header/page-header.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { AdminCreateModalComponent } from '@app/components/admin/users/admin-create-modal/admin-create-modal.component';
import { IAdminChoirUserListItem, IAdminChoirUsersFilter } from '@models/admin-models/admin-choir-user-list-item.model';
import { IAdminEventUserListItem, IAdminEventUsersFilter } from '@models/admin-models/admin-event-user-list-item.model';
import { IAdminUserListItem, IAdminUsersFilter } from '@models/admin-models/admin-user-list-item.model';
import { IAdminUnattachedUserListItem } from '@models/admin-models/admin-unattached-user-list-item.model';
import { ISelectOption } from '@models/common-models/select-option.model';
import { UserRoleEnum, getUserRoleLabel, getUserRolesLabel } from '@app/enums/user-role.enum';
import { MemberStatusEnum, getMemberStatusLabel } from '@app/enums/member-status.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { AttendanceEnum, getPresenceLabel } from '@app/enums/presence.enum';

const FILTER_DEBOUNCE_MS = 300;

export type UserTab = 'choirs' | 'events' | 'admins' | 'unattached';

const VALID_TABS: readonly UserTab[] = ['choirs', 'events', 'admins', 'unattached'];

// Query param 'tab' (voir CORRECTION CIBLÉE — dashboard.component.ts n'émet aujourd'hui
// aucun `onglet`, seulement IsActive/IsGuestAccount ; ce paramètre reste géré pour toute
// navigation future ou lien partagé qui le fournirait explicitement). Valeur hors des 4 clés
// connues → ignorée silencieusement, jamais d'exception.
function parseTabQueryParam(paramMap: ParamMap): UserTab | undefined {
  const raw = paramMap.get('tab');
  return raw !== null && (VALID_TABS as readonly string[]).includes(raw) ? (raw as UserTab) : undefined;
}

// Union des 4 lignes affichables — UNE SEULE instance de DataTableComponent est utilisée pour
// les 4 onglets (exigence explicite de non-duplication), paramétrée dynamiquement par
// activeTab(). Chaque interface porte un champ discriminant unique permettant de narrower le
// type dans les gabarits de cellule sans jamais recourir à `any` :
// Roles (chorales), EventId (événements), CreatedByUserId (administrateurs),
// IsGuestAccount (sans rattachement).
export type AdminUserRow =
  | IAdminChoirUserListItem
  | IAdminEventUserListItem
  | IAdminUserListItem
  | IAdminUnattachedUserListItem;

function isChoirRow(row: AdminUserRow): row is IAdminChoirUserListItem {
  return 'Roles' in row;
}

function isEventRow(row: AdminUserRow): row is IAdminEventUserListItem {
  return 'EventId' in row;
}

function isAdminRow(row: AdminUserRow): row is IAdminUserListItem {
  return 'CreatedByUserId' in row;
}

function isUnattachedRow(row: AdminUserRow): row is IAdminUnattachedUserListItem {
  return 'IsGuestAccount' in row;
}

// Statut (MemberStatusEnum) n'existe que sur les rattachements chorale/événement.
function hasStatus(row: AdminUserRow): row is IAdminChoirUserListItem | IAdminEventUserListItem {
  return isChoirRow(row) || isEventRow(row);
}

// IsActive existe partout SAUF sur un rattachement événement (qui n'a que Statut/Presence).
function hasIsActive(row: AdminUserRow): row is IAdminChoirUserListItem | IAdminUserListItem | IAdminUnattachedUserListItem {
  return !isEventRow(row);
}

// CreatedAt/LastConnection n'existent que sur les onglets Administrateurs et Sans rattachement.
function hasCreatedAtEtLastConnection(row: AdminUserRow): row is IAdminUserListItem | IAdminUnattachedUserListItem {
  return isAdminRow(row) || isUnattachedRow(row);
}

// Liste paginée des users de la zone admin — 4 onglets sur UNE SEULE page (décision
// produit, pas 4 routes). Une ligne = un RATTACHEMENT, jamais une personne : une même personne
// membre de 2 chorales et participante d'1 événement produit 3 lignes réparties sur 2 onglets.
// La déduplication se fait dans la fiche (UserDetailComponent), qui agrège tous les
// rattachements d'une personne et où vivent TOUTES les actions — AUCUNE action sur une ligne
// de ce tableau (une action sur une ligne suggérerait qu'on agit sur le rattachement plutôt
// que sur le compte entier).
//
// Pagination et tri sont partagés par l'onglet actif et remis à zéro à chaque changement
// d'onglet (sinon rester en page 4 d'un onglet qui n'en a que 2 affiche une liste vide
// incompréhensible). Les filtres avancés (chorale/rôle/statut/voix/actif pour l'onglet
// Chorales, événement/rôle/présence/à venir pour l'onglet Événements) sont des signaux
// dédiés par onglet — ils ne peuvent donc jamais fuiter d'un onglet à l'autre (un filtre
// Voix=Soprano n'a aucun sens sur l'onglet Administrateurs).
//
// Sélection de chorales/événements : modale de recherche par nom (EntityMultiSelectModalComponent,
// searchChoirs/searchEvents ci-dessous), pas de saisie d'UUID — ChoirIds/EventIds transmis en
// paramètres répétés (AdminUserService.getChoirUsersPaged/getEventUsersPaged).
@Component({
  selector: 'app-user-list',
  standalone: true,
  imports: [DatePipe, DataTableComponent, PageHeaderComponent, IconComponent, AdminCreateModalComponent, EntityMultiSelectModalComponent],
  templateUrl: './user-list.component.html',
  styleUrl: './user-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserListComponent {
  private readonly adminUserService = inject(AdminUserService);
  private readonly adminChoirService = inject(AdminChoirService);
  private readonly adminEventService = inject(AdminEventService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly MemberStatusEnum = MemberStatusEnum;
  protected readonly getUserRoleLabel = getUserRoleLabel;
  protected readonly getUserRolesLabel = getUserRolesLabel;
  protected readonly getMemberStatusLabel = getMemberStatusLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getPresenceLabel = getPresenceLabel;
  protected readonly isChoirRow = isChoirRow;
  protected readonly isEventRow = isEventRow;
  protected readonly isAdminRow = isAdminRow;
  protected readonly isUnattachedRow = isUnattachedRow;
  protected readonly hasStatus = hasStatus;
  protected readonly hasIsActive = hasIsActive;
  protected readonly hasCreatedAtEtLastConnection = hasCreatedAtEtLastConnection;

  // --- Gabarits de cellule (toujours présents dans le gabarit, jamais sous *ngIf/@if — les
  // requêtes de vue signal doivent pouvoir se résoudre indépendamment de l'onglet actif).
  private readonly tplRoles = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplRoles');
  private readonly tplVoicePart = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplVoix');
  private readonly tplStatus = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplStatus');
  private readonly tplActive = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplActif');
  private readonly tplLastActive = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplLastActive');
  private readonly tplEventDate = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplEvenementDate');
  private readonly tplChoirPorteuse = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplChoralePorteuse');
  private readonly tplRoleUnique = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplRoleUnique');
  private readonly tplPresence = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplPresence');
  private readonly tplLastConnection = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplLastConnection');
  private readonly tplCreatedAt = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplCreatedAt');
  private readonly tplCreatedBy = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplCreatedBy');
  private readonly tplGuest = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplGuest');
  private readonly tplClientAssignment = viewChild<TemplateRef<{ $implicit: AdminUserRow }>>('tplClientAssignment');

  // Résolution de l'onglet initial depuis les query params de la navigation (tuiles "Active" /
  // "Invités non activés" du tableau de bord admin, voir dashboard.component.ts) —
  // ActivatedRouteSnapshot, lecture unique au chargement. Priorité : 'tab' explicite si
  // présent et reconnu (voir parseTabQueryParam) ; sinon IsGuestAccount (n'a de sens que sur
  // l'onglet Sans rattachement) ; sinon IsActive seul (onglet Administrateurs, faute d'onglet
  // agrégeant tous les comptes) ; sinon l'onglet par défaut inchangé ('choirs'). Toute
  // combinaison incohérente (ex. tab=choirs avec IsGuestAccount) est silencieusement sans
  // effet : IsActive/IsGuestAccount ne sont lus que pour l'onglet effectivement retenu ici.
  private readonly initialQueryParamMap = this.route.snapshot.queryParamMap;
  private readonly initialTabFromQueryParams: UserTab = (() => {
    const explicit = parseTabQueryParam(this.initialQueryParamMap);
    if (explicit) return explicit;
    if (parseTriStateBooleanQueryParam(this.initialQueryParamMap, 'IsGuestAccount') !== '') return 'unattached';
    if (parseTriStateBooleanQueryParam(this.initialQueryParamMap, 'IsActive') !== '') return 'admins';
    return 'choirs';
  })();

  readonly activeTab = signal<UserTab>(this.initialTabFromQueryParams);

  // Pagination/tri/filtre texte : partagés par l'onglet actif, remis à zéro à chaque
  // changement d'onglet (selectTab).
  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly filterText = signal('');

  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  // Items par onglet — jamais partagés, jamais réinitialisés au changement d'onglet (on
  // garde le dernier résultat chargé à l'écran le temps du rechargement suivant).
  private readonly choirItems = signal<IAdminChoirUserListItem[]>([]);
  private readonly eventItems = signal<IAdminEventUserListItem[]>([]);
  private readonly adminItems = signal<IAdminUserListItem[]>([]);
  private readonly unattachedItems = signal<IAdminUnattachedUserListItem[]>([]);

  // Filtres avancés onglet Chorales — jamais lus par un autre onglet. choirFilterChoirs porte
  // à la fois les Id (filtre réel) et les Label (chips affichées au-dessus du tableau) : garder
  // les deux ensemble évite un second appel réseau rien que pour résoudre un nom de chorale.
  readonly choirFilterChoirs = signal<ISelectOption<string>[]>([]);
  readonly choirFilterRole = signal<UserRoleEnum | ''>('');
  readonly choirFilterStatus = signal<MemberStatusEnum | ''>('');
  readonly choirFilterVoicePart = signal<VoicePartEnum | ''>('');
  readonly choirFilterIsActive = signal<'' | 'true' | 'false'>('');
  // Regroupement d'affichage (client, page courante uniquement) — PAS un filtre avancé : remis
  // à false à chaque changement d'onglet (voir selectTab), contrairement aux filtres ci-dessus.
  readonly groupByChoirEnabled = signal(false);

  // Filtres avancés onglet Événements — jamais lus par un autre onglet.
  readonly eventFilterEvents = signal<ISelectOption<string>[]>([]);
  readonly eventFilterRole = signal<UserRoleEnum | ''>('');
  readonly eventFilterPresence = signal<AttendanceEnum | ''>('');
  readonly eventFilterUpcoming = signal<'' | 'true' | 'false'>('');
  readonly groupByEventEnabled = signal(false);

  // Ouverture des modales de sélection (Spec : remplace les champs UUID bruts).
  readonly showChoirPickerModal = signal(false);
  readonly showEventPickerModal = signal(false);

  // Filtres avancés onglet Administrateurs — jamais lus par un autre onglet. Valeur initiale
  // IsActive appliquée UNIQUEMENT si initialTabFromQueryParams a effectivement résolu cet
  // onglet (voir activeTab ci-dessus) : IsActive seul, sans 'tab' ni IsGuestAccount, est
  // routé ici par convention. Pas de contrôle UI pour le update après coup aujourd'hui (aucun
  // sélecteur IsActive n'existait sur cet onglet avant ce correctif — en ajouter un est une
  // évolution de mise en page hors du périmètre de ce raccordement ciblé, signalé au rapport).
  readonly administrateursFilterIsActive = signal<'' | 'true' | 'false'>(
    this.initialTabFromQueryParams === 'admins' ? parseTriStateBooleanQueryParam(this.initialQueryParamMap, 'IsActive') : ''
  );

  // Filtres avancés onglet Sans rattachement — jamais lus par un autre onglet. Même remarque
  // que ci-dessus (IsActive ET IsGuestAccount, tuile "Invités non activés" du tableau de bord).
  readonly unattachedFilterIsActive = signal<'' | 'true' | 'false'>(
    this.initialTabFromQueryParams === 'unattached' ? parseTriStateBooleanQueryParam(this.initialQueryParamMap, 'IsActive') : ''
  );
  readonly unattachedFilterIsGuestAccount = signal<'' | 'true' | 'false'>(
    this.initialTabFromQueryParams === 'unattached'
      ? parseTriStateBooleanQueryParam(this.initialQueryParamMap, 'IsGuestAccount')
      : ''
  );

  readonly showCreateAdminModal = signal(false);

  protected readonly allRoles: UserRoleEnum[] = [
    UserRoleEnum.Admin,
    UserRoleEnum.SectionLeader,
    UserRoleEnum.Singer,
    UserRoleEnum.Manager,
    UserRoleEnum.Organizer,
    UserRoleEnum.Participant,
    UserRoleEnum.ClientManager
  ];
  protected readonly allStatuss: MemberStatusEnum[] = [
    MemberStatusEnum.Invited,
    MemberStatusEnum.Active,
    MemberStatusEnum.Inactive,
    MemberStatusEnum.Archived
  ];
  protected readonly allVoicePart: VoicePartEnum[] = [VoicePartEnum.Soprano, VoicePartEnum.Alto, VoicePartEnum.Tenor, VoicePartEnum.Bass];
  protected readonly allPresences: AttendanceEnum[] = [
    AttendanceEnum.NoReply,
    AttendanceEnum.Attending,
    AttendanceEnum.Maybe,
    AttendanceEnum.NotAttending
  ];

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  // Items de l'onglet actif — la seule source liée à [items] de l'unique <app-data-table>.
  readonly currentItems = computed<AdminUserRow[]>(() => {
    switch (this.activeTab()) {
      case 'choirs':
        return this.choirItems();
      case 'events':
        return this.eventItems();
      case 'admins':
        return this.adminItems();
      case 'unattached':
        return this.unattachedItems();
    }
  });

  readonly currentColumns = computed<IDataTableColumn<AdminUserRow>[]>(() => {
    switch (this.activeTab()) {
      case 'choirs':
        return [
          { key: 'Lastname', label: 'Nom', sortable: true },
          { key: 'Firstname', label: 'Prénom', sortable: true },
          { key: 'Email', label: 'Email', sortable: true },
          { key: 'ChoirName', label: 'Chorale', sortable: true },
          { key: 'Roles', label: 'Rôle(s)', cellTemplate: this.tplRoles() },
          { key: 'PrimaryVoicePart', label: 'Voix principale', cellTemplate: this.tplVoicePart() },
          { key: 'Status', label: 'Statut', cellTemplate: this.tplStatus() },
          { key: 'IsActive', label: 'Compte actif', cellTemplate: this.tplActive() },
          { key: 'LastActive', label: 'Dernière activité', cellTemplate: this.tplLastActive() }
        ];
      case 'events':
        return [
          { key: 'Lastname', label: 'Nom', sortable: true },
          { key: 'Firstname', label: 'Prénom', sortable: true },
          { key: 'Email', label: 'Email', sortable: true },
          { key: 'EventTitle', label: 'Événement', sortable: true },
          { key: 'EventStartDate', label: 'Date', sortable: true, cellTemplate: this.tplEventDate() },
          { key: 'ChoirName', label: 'Chorale porteuse', cellTemplate: this.tplChoirPorteuse() },
          { key: 'Role', label: 'Rôle', cellTemplate: this.tplRoleUnique() },
          { key: 'Presence', label: 'Présence', cellTemplate: this.tplPresence() },
          { key: 'Status', label: 'Statut', cellTemplate: this.tplStatus() }
        ];
      case 'admins':
        return [
          { key: 'Lastname', label: 'Nom', sortable: true },
          { key: 'Firstname', label: 'Prénom', sortable: true },
          { key: 'Email', label: 'Email', sortable: true },
          { key: 'IsActive', label: 'Compte actif', cellTemplate: this.tplActive() },
          { key: 'LastConnection', label: 'Dernière connexion', sortable: true, cellTemplate: this.tplLastConnection() },
          { key: 'CreatedAt', label: 'Créé le', sortable: true, cellTemplate: this.tplCreatedAt() },
          { key: 'CreatedByName', label: 'Créé par', cellTemplate: this.tplCreatedBy() }
        ];
      case 'unattached':
        return [
          { key: 'Lastname', label: 'Nom', sortable: true },
          { key: 'Firstname', label: 'Prénom', sortable: true },
          { key: 'Email', label: 'Email', sortable: true },
          { key: 'IsActive', label: 'Compte actif', cellTemplate: this.tplActive() },
          { key: 'IsGuestAccount', label: 'Compte invité', cellTemplate: this.tplGuest() },
          { key: 'ClientName', label: 'Rattachement client', cellTemplate: this.tplClientAssignment() },
          { key: 'CreatedAt', label: 'Créé le', sortable: true, cellTemplate: this.tplCreatedAt() },
          { key: 'LastConnection', label: 'Dernière connexion', sortable: true, cellTemplate: this.tplLastConnection() }
        ];
    }
  });

  // Chips affichées au-dessus du tableau (Spec) — uniquement pour l'onglet dont elles
  // proviennent, jamais un mélange des deux (même garde-fou que les filtres avancés eux-mêmes).
  readonly currentActiveFilters = computed<IDataTableChip[]>(() => {
    switch (this.activeTab()) {
      case 'choirs':
        return this.choirFilterChoirs().map(option => ({ key: option.Value, label: option.Label }));
      case 'events':
        return this.eventFilterEvents().map(option => ({ key: option.Value, label: option.Label }));
      default:
        return [];
    }
  });

  // Regroupement d'affichage de l'unique <app-data-table> — purement client, sur la page déjà
  // reçue (voir DataTableComponent.groupBy) : chorale sur l'onglet Chorales, événement sur
  // l'onglet Événements, jamais sur Administrateurs/Sans rattachement (aucune donnée de
  // regroupement pertinente sur ces deux onglets).
  readonly currentGroupBy = computed<DataTableGroupByFn<AdminUserRow> | null>(() => {
    if (this.activeTab() === 'choirs' && this.groupByChoirEnabled()) {
      return item => (isChoirRow(item) ? { key: item.ChoirId, label: item.ChoirName } : { key: '', label: '' });
    }
    if (this.activeTab() === 'events' && this.groupByEventEnabled()) {
      return item => (isEventRow(item) ? { key: item.EventId, label: item.EventTitle } : { key: '', label: '' });
    }
    return null;
  });

  // Adaptateurs domaine -> ISelectOption<string> consommés par EntityMultiSelectModalComponent
  // (modale partagée, agnostique du domaine — voir entity-multi-select-modal.component.ts).
  // Champs déclarés en classe (pas des méthodes) pour rester des références stables passées en
  // `[searchFn]`, même convention que `submitCreate` dans client-list.component.ts.
  protected readonly searchChoirs: EntitySearchFn = pagination =>
    this.adminChoirService
      .getPaged(pagination, {})
      .pipe(map(result => ({ ...result, Items: result.Items.map(choir => ({ Value: choir.Id, Label: choir.Name })) })));

  protected readonly searchEvents: EntitySearchFn = pagination =>
    this.adminEventService
      .getPaged(pagination, {})
      .pipe(map(result => ({ ...result, Items: result.Items.map(event => ({ Value: event.Id, Label: event.Title })) })));

  // Anti-rebond sur le filtre texte (300 ms), transmis par app-data-table (filterChange).
  private readonly debouncedLoad = debounce(() => this.load(), FILTER_DEBOUNCE_MS);

  constructor() {
    this.load();
  }

  // Changement d'onglet : remise à zéro OBLIGATOIRE de la pagination/tri/filtre texte —
  // sinon rester en page 4 d'un onglet qui n'en a que 2 affiche une liste vide et
  // incompréhensible. Les filtres avancés (par onglet) ne sont volontairement pas touchés
  // ici : ils appartiennent à un signal dédié à l'onglet quitté et ne peuvent donc jamais
  // fuiter vers l'onglet ouvert.
  selectTab(tab: UserTab): void {
    if (this.activeTab() === tab) return;
    this.activeTab.set(tab);
    this.page.set(1);
    this.sortActive.set(undefined);
    this.sortDirection.set(undefined);
    this.filterText.set('');
    this.error.set(null);
    this.groupByChoirEnabled.set(false);
    this.groupByEventEnabled.set(false);
    this.load();
  }

  onFilterChange(value: string): void {
    this.filterText.set(value);
    this.page.set(1);
    this.debouncedLoad();
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

  // Onglets Chorales/Événements : navigation sur UserId (identifiant de la personne).
  // Onglets Administrateurs/Sans rattachement : Id EST déjà l'identifiant de la personne
  // (ce ne sont pas le même champ — voir modèles).
  onRowClick(row: AdminUserRow): void {
    const userId = isChoirRow(row) || isEventRow(row) ? row.UserId : row.Id;
    this.router.navigate(['/', RoutePaths.Admin, RoutePaths.AdminUsers, userId]);
  }

  onAdvancedFilterChange(): void {
    this.page.set(1);
    this.load();
  }

  // Les <select> transmettent une valeur en chaîne — conversion explicite ici plutôt que dans
  // le gabarit (Number/Math/String ne sont pas résolubles dans une expression de template
  // Angular, seuls les membres du composant le sont).
  onChoirRoleChange(value: string): void {
    this.choirFilterRole.set(value === '' ? '' : (Number(value) as UserRoleEnum));
    this.onAdvancedFilterChange();
  }

  onChoirStatusChange(value: string): void {
    this.choirFilterStatus.set(value === '' ? '' : (Number(value) as MemberStatusEnum));
    this.onAdvancedFilterChange();
  }

  onChoirVoicePartChange(value: string): void {
    this.choirFilterVoicePart.set(value === '' ? '' : (Number(value) as VoicePartEnum));
    this.onAdvancedFilterChange();
  }

  onChoirIsActiveChange(value: string): void {
    this.choirFilterIsActive.set(value as '' | 'true' | 'false');
    this.onAdvancedFilterChange();
  }

  onEventRoleChange(value: string): void {
    this.eventFilterRole.set(value === '' ? '' : (Number(value) as UserRoleEnum));
    this.onAdvancedFilterChange();
  }

  onEventPresenceChange(value: string): void {
    this.eventFilterPresence.set(value === '' ? '' : (Number(value) as AttendanceEnum));
    this.onAdvancedFilterChange();
  }

  onEventUpcomingChange(value: string): void {
    this.eventFilterUpcoming.set(value as '' | 'true' | 'false');
    this.onAdvancedFilterChange();
  }

  // Retrait d'une chip (Spec) — la clé est l'Id de la chorale/l'événement (voir
  // currentActiveFilters), jamais lu par un onglet autre que celui dont elle provient.
  onChipRemove(key: string): void {
    if (this.activeTab() === 'choirs') {
      this.choirFilterChoirs.update(current => current.filter(option => option.Value !== key));
    } else if (this.activeTab() === 'events') {
      this.eventFilterEvents.update(current => current.filter(option => option.Value !== key));
    }
    this.onAdvancedFilterChange();
  }

  openChoirPickerModal(): void {
    this.showChoirPickerModal.set(true);
  }

  onChoirsSelected(selection: ISelectOption<string>[]): void {
    this.choirFilterChoirs.set(selection);
    this.showChoirPickerModal.set(false);
    this.onAdvancedFilterChange();
  }

  openEventPickerModal(): void {
    this.showEventPickerModal.set(true);
  }

  onEventsSelected(selection: ISelectOption<string>[]): void {
    this.eventFilterEvents.set(selection);
    this.showEventPickerModal.set(false);
    this.onAdvancedFilterChange();
  }

  openCreateAdminModal(): void {
    this.showCreateAdminModal.set(true);
  }

  onAdminCreated(): void {
    this.showCreateAdminModal.set(false);
    if (this.activeTab() === 'admins') {
      this.load();
    }
  }

  onCreateAdminCancelled(): void {
    this.showCreateAdminModal.set(false);
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);

    const pagination = {
      Page: this.page(),
      PageSize: this.pageSize(),
      SortActive: this.sortActive(),
      SortDirection: this.sortDirection(),
      Filter: this.filterText() || undefined
    };

    switch (this.activeTab()) {
      case 'choirs':
        this.loadChoirs(pagination);
        break;
      case 'events':
        this.loadEvents(pagination);
        break;
      case 'admins':
        this.loadAdministrateurs(pagination);
        break;
      case 'unattached':
        this.loadUnattached(pagination);
        break;
    }
  }

  private loadChoirs(pagination: { Page: number; PageSize: number; SortActive?: string; SortDirection?: 'asc' | 'desc'; Filter?: string }): void {
    // Valeurs lues une seule fois dans des constantes locales : TypeScript ne peut pas
    // rapprocher le résultat d'un narrowing (=== '') d'un second appel du même signal getter
    // dans la branche "else" d'un ternaire (deux invocations distinctes, non prouvées pures).
    const role = this.choirFilterRole();
    const status = this.choirFilterStatus();
    const voicePart = this.choirFilterVoicePart();
    const isActive = this.choirFilterIsActive();

    const choirIds = this.choirFilterChoirs().map(option => option.Value);

    const filter: IAdminChoirUsersFilter = {
      ChoirIds: choirIds.length > 0 ? choirIds : undefined,
      Role: role === '' ? undefined : role,
      Status: status === '' ? undefined : status,
      VoicePart: voicePart === '' ? undefined : voicePart,
      IsActive: isActive === '' ? undefined : isActive === 'true'
    };

    this.adminUserService
      .getChoirUsersPaged(pagination, filter)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.choirItems.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les users de chorale. Merci de réessayer.');
        }
      });
  }

  private loadEvents(pagination: { Page: number; PageSize: number; SortActive?: string; SortDirection?: 'asc' | 'desc'; Filter?: string }): void {
    const role = this.eventFilterRole();
    const presence = this.eventFilterPresence();
    const upcoming = this.eventFilterUpcoming();

    const eventIds = this.eventFilterEvents().map(option => option.Value);

    const filter: IAdminEventUsersFilter = {
      EventIds: eventIds.length > 0 ? eventIds : undefined,
      Role: role === '' ? undefined : role,
      Presence: presence === '' ? undefined : presence,
      Upcoming: upcoming === '' ? undefined : upcoming === 'true'
    };

    this.adminUserService
      .getEventUsersPaged(pagination, filter)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.eventItems.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les users des événements. Merci de réessayer.');
        }
      });
  }

  private loadAdministrateurs(pagination: {
    Page: number;
    PageSize: number;
    SortActive?: string;
    SortDirection?: 'asc' | 'desc';
    Filter?: string;
  }): void {
    const isActive = this.administrateursFilterIsActive();
    const filter: IAdminUsersFilter = { IsActive: isActive === '' ? undefined : isActive === 'true' };

    this.adminUserService
      .getPaged(pagination, filter)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.adminItems.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les administrateurs. Merci de réessayer.');
        }
      });
  }

  private loadUnattached(pagination: {
    Page: number;
    PageSize: number;
    SortActive?: string;
    SortDirection?: 'asc' | 'desc';
    Filter?: string;
  }): void {
    const isActive = this.unattachedFilterIsActive();
    const isGuestAccount = this.unattachedFilterIsGuestAccount();
    const filter: IAdminUsersFilter = {
      IsActive: isActive === '' ? undefined : isActive === 'true',
      IsGuestAccount: isGuestAccount === '' ? undefined : isGuestAccount === 'true'
    };

    this.adminUserService
      .getUnattachedUsersPaged(pagination, filter)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.unattachedItems.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les comptes sans rattachement. Merci de réessayer.');
        }
      });
  }
}
