import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthStore } from '@core/auth.store';
import { DisplayedZoneStore } from '@core/displayed-zone.store';
import { displayedZoneLabel } from '@core/displayed-zone';
import { RoutePaths, managementPath } from '@core/route-paths';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { ISidebarNavItem } from '@models/common-models/sidebar-nav-item.model';
import { MembershipRequestManagementService } from '@app/services/onboarding/membership-request-management.service';

// DisplayedZoneStore fournit la zone et l'identifiant affichés ; AuthStore.activeSpaceType()
// détermine si cet espace est une chorale. Les contenus chorale et le badge des demandes
// d'adhésion en attente sont masqués pour tout autre type d'espace.
function buildManagementNavItems(spaceId: string, spaceType: SpaceTypeEnum | null, requestsBadge: number): ISidebarNavItem[] {
  const items: ISidebarNavItem[] = [
    { Label: 'Tableau de bord', Path: managementPath(spaceId, RoutePaths.Dashboard), Icon: IconNameEnum.ChartBar }
  ];

  if (spaceType === SpaceTypeEnum.Choir) {
    items.push(
      {
        Label: 'Membres',
        Path: managementPath(spaceId, RoutePaths.Members),
        Icon: IconNameEnum.Users,
        RequiredRoles: [UserRoleEnum.Manager],
        Badge: requestsBadge > 0 ? requestsBadge : undefined
      },
      {
        Label: 'Chants',
        Path: managementPath(spaceId, RoutePaths.Songs),
        Icon: IconNameEnum.MusicNotes,
        Children: [
          { Label: 'Partitions', Path: managementPath(spaceId, RoutePaths.Scores), Icon: IconNameEnum.FilePdf },
          { Label: 'Enregistrements', Path: managementPath(spaceId, RoutePaths.Recordings), Icon: IconNameEnum.FileMusic }
        ]
      },
      {
        Label: 'Événements',
        Path: managementPath(spaceId, RoutePaths.Events),
        Icon: IconNameEnum.Calendar,
        Children: [{ Label: 'Listes', Path: managementPath(spaceId, RoutePaths.SongLists), Icon: IconNameEnum.List }]
      }
    );
  }

  // Pas d'entrée « Consignes » : une consigne vit dans l'écran de son chant, jamais dans un
  // écran transverse (décision produit, Spec/chorale/10-decisions.md).
  return items;
}

function buildMemberNavItems(spaceType: SpaceTypeEnum | null): ISidebarNavItem[] {
  const items: ISidebarNavItem[] = [{ Label: 'Accueil', Path: `/${RoutePaths.Me}`, Icon: IconNameEnum.House }];
  if (spaceType === SpaceTypeEnum.Choir) {
    items.push({ Label: 'Chants', Path: `/${RoutePaths.Me}/${RoutePaths.Songs}`, Icon: IconNameEnum.MusicNotes });
  }
  return items;
}

const NAV_ITEMS_ADMIN: ISidebarNavItem[] = [
  { Label: 'Tableau de bord', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminDashboard}`, Icon: IconNameEnum.ChartBar },
  { Label: 'Clients', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminClients}`, Icon: IconNameEnum.Buildings },
  { Label: 'Chorales', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminChoirs}`, Icon: IconNameEnum.MusicNotes },
  { Label: 'Événements', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminEvents}`, Icon: IconNameEnum.Calendar },
  { Label: 'Utilisateurs', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminUsers}`, Icon: IconNameEnum.Users },
  { Label: 'Chants', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminSongs}`, Icon: IconNameEnum.FileMusic },
  { Label: 'Audit', Path: `/${RoutePaths.Admin}/${RoutePaths.AdminAudit}`, Icon: IconNameEnum.ShieldCheck }
];

// Sidebar fixe 256px, rétractable en icônes seules à partir de desktop (Spec §5.2,
// ChoralFront/CLAUDE.md). Un seul composant pour les 4 zones (exigence de non-duplication) :
// le menu affiché dépend de DisplayedZoneStore.zone() — /admin, /management/:spaceId (variable
// selon le type d'espace) et /client/:clientId (Ma structure) ont leur propre jeu d'items ;
// /me expose l'accueil et le répertoire en lecture seule.
// spaceRoleGuard reste la source de vérité UX pour le filtre des items — cette liste applique
// le même filtre pour ne pas afficher de liens menant à un guard qui bloquerait l'accès.
@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, IconComponent],
  templateUrl: './sidebar.component.html',
  styleUrl: './sidebar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SidebarComponent {
  private readonly authStore = inject(AuthStore);
  private readonly displayedZoneStore = inject(DisplayedZoneStore);
  private readonly membershipRequestManagementService = inject(MembershipRequestManagementService);
  private readonly destroyRef = inject(DestroyRef);

  readonly collapsed = input<boolean>(false);
  // Ouverture du tiroir sous 1024 px — sans effet au-dessus, où la barre reste fixe.
  readonly mobileOpen = input<boolean>(false);
  readonly toggleCollapse = output();
  readonly closeRequested = output();

  protected readonly IconNameEnum = IconNameEnum;

  private readonly expandedSections = signal<Set<string>>(new Set());

  // Count de demandes d'adhésion Pending pour l'espace de management actif (badge "Membres",
  // lot 6 onboarding) — rechargé à chaque changement d'espace actif ou de rôle courant.
  private readonly requestsBadge = signal(0);

  constructor() {
    effect(() => {
      const zone = this.displayedZoneStore.zone();
      const isManagerChoir =
        zone.kind === 'management' &&
        this.authStore.activeSpaceType() === SpaceTypeEnum.Choir &&
        (this.authStore.isGlobalAdmin() || this.authStore.activeSpaceRoles().includes(UserRoleEnum.Manager));

      if (!isManagerChoir || !zone.spaceId) {
        this.requestsBadge.set(0);
        return;
      }

      this.membershipRequestManagementService
        .getPendingCount(zone.spaceId)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: count => this.requestsBadge.set(count),
          error: () => this.requestsBadge.set(0)
        });
    });
  }

  readonly navItems = computed<ISidebarNavItem[]>(() => {
    const zone = this.displayedZoneStore.zone();
    const isAdmin = this.authStore.isGlobalAdmin();
    const currentRoles = this.authStore.activeSpaceRoles();

    let items: ISidebarNavItem[];
    switch (zone.kind) {
      case 'admin':
        items = NAV_ITEMS_ADMIN;
        break;
      case 'management':
        items = zone.spaceId ? buildManagementNavItems(zone.spaceId, this.authStore.activeSpaceType(), this.requestsBadge()) : [];
        break;
      case 'member':
        items = buildMemberNavItems(this.authStore.activeSpaceType());
        break;
      case 'client':
        // Un seul écran aujourd'hui (Ma structure) — l'écran de détail chorale
        // (choirs/:choirId) est atteint depuis son tableau, pas depuis un lien de menu dédié.
        items = zone.clientId
          ? [{ Label: 'Ma structure', Path: `/${RoutePaths.Client}/${zone.clientId}`, Icon: IconNameEnum.Buildings }]
          : [];
        break;
      default:
        items = [];
    }

    return items.filter(item => {
      if (!item.RequiredRoles) return true;
      if (isAdmin) return true;
      return item.RequiredRoles.some(role => currentRoles.includes(role));
    });
  });

  // Libellé de l'en-tête : dérivé de la zone AFFICHÉE (URL courante), jamais d'un repli sur
  // AuthStore.activeSpaceId — voir core/displayed-zone.ts.
  readonly activeSpaceName = computed(() =>
    displayedZoneLabel(this.displayedZoneStore.zone(), this.authStore.spaceRoles(), this.authStore.clientRoles())
  );

  isSectionExpanded(label: string): boolean {
    return this.expandedSections().has(label);
  }

  toggleSection(label: string): void {
    this.expandedSections.update(set => {
      const next = new Set(set);
      if (next.has(label)) {
        next.delete(label);
      } else {
        next.add(label);
      }
      return next;
    });
  }

  onToggleCollapse(): void {
    this.toggleCollapse.emit();
  }

  onClose(): void {
    this.closeRequested.emit();
  }

}
