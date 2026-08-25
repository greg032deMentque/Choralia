import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  computed,
  inject,
  input,
  output,
  signal
} from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthStore } from '@core/auth.store';
import { AuthService } from '@app/services/auth/auth.service';
import { StorageService } from '@app/services/storage.service';
import { RoutePaths, managementPath } from '@core/route-paths';
import { MANAGEMENT_ROLES } from '@core/zone-resolver';
import { DisplayedZoneStore } from '@core/displayed-zone.store';
import { displayedZoneLabel } from '@core/displayed-zone';
import { userRoleFromString, UserRoleEnum } from '@app/enums/user-role.enum';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';

@Component({
  selector: 'app-topbar',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './topbar.component.html',
  styleUrl: './topbar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TopbarComponent {
  private readonly authStore = inject(AuthStore);
  private readonly authService = inject(AuthService);
  private readonly storage = inject(StorageService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly displayedZoneStore = inject(DisplayedZoneStore);

  protected readonly IconNameEnum = IconNameEnum;

  // Ouverture du tiroir de navigation (sous 1024 px). L'état appartient au shell : la topbar
  // ne fait que le refléter et demander sa bascule.
  readonly navOpen = input<boolean>(false);
  readonly toggleNav = output();

  readonly user = this.authStore.user;
  // Sélecteur d'espace : TOUS les espaces (chorales et événements), pas seulement les
  // chorales — change d'espace actif peut change de zone (/management <-> /moi), voir
  // AuthStore.currentZone(). clientRoles alimente un second groupe distinct (« Ma structure »)
  // dans le même sélecteur.
  readonly spaceRoles = this.authStore.spaceRoles;
  readonly clientRoles = this.authStore.clientRoles;

  readonly hasSpaceGroup = computed(() => this.spaceRoles().length > 0);
  readonly hasClientGroup = computed(() => this.clientRoles().length > 0);
  readonly choirSpaces = computed(() => this.spaceRoles().filter(space => space.SpaceType === SpaceTypeEnum.Choir));
  readonly eventSpaces = computed(() => this.spaceRoles().filter(space => space.SpaceType === SpaceTypeEnum.Event));
  readonly showSelector = computed(() => this.hasSpaceGroup() || this.hasClientGroup());

  // Libellé affiché : dérivé de la zone AFFICHÉE (URL courante), jamais d'un repli sur
  // AuthStore.activeSpaceId — voir core/displayed-zone.ts.
  readonly activeSpaceName = computed(() =>
    displayedZoneLabel(this.displayedZoneStore.zone(), this.spaceRoles(), this.clientRoles())
  );
  readonly userInitials = computed(() => {
    const currentUser = this.user();
    if (!currentUser) return '';
    return `${currentUser.Firstname.charAt(0)}${currentUser.Lastname.charAt(0)}`.toUpperCase();
  });

  readonly isSpaceMenuOpen = signal(false);
  readonly isUserMenuOpen = signal(false);

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target as Node)) {
      this.isSpaceMenuOpen.set(false);
      this.isUserMenuOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscapeKeydown(): void {
    this.isSpaceMenuOpen.set(false);
    this.isUserMenuOpen.set(false);
  }

  onToggleNav(): void {
    this.toggleNav.emit();
  }

  toggleSpaceMenu(): void {
    this.isSpaceMenuOpen.update(v => !v);
    this.isUserMenuOpen.set(false);
  }

  toggleUserMenu(): void {
    this.isUserMenuOpen.update(v => !v);
    this.isSpaceMenuOpen.set(false);
  }

  // Changer d'espace actif change potentiellement de zone : un espace où l'utilisateur n'a
  // qu'un rôle d'appartenance simple envoie vers /moi, un espace où il a un rôle de management
  // (Responsable/SectionLeader/Organizer) envoie vers /management/:spaceId — jamais un simple
  // changement de filtre sur l'écran courant.
  selectSpace(space: ISpaceRoleAssignment): void {
    this.isSpaceMenuOpen.set(false);
    this.authStore.setActiveSpace(space.SpaceId);

    const roles = space.Roles.map(role => userRoleFromString(role)).filter((role): role is UserRoleEnum => role !== null);
    const target = roles.some(role => MANAGEMENT_ROLES.includes(role))
      ? managementPath(space.SpaceId, RoutePaths.Dashboard)
      : `/${RoutePaths.Me}`;

    this.router.navigateByUrl(target);
  }

  // Un client (« Ma structure ») n'est PAS un Space (10-D23) : navigation seule, jamais
  // setActiveSpace — l'espace actif (donc le scope X-Space-Id envoyé sur les zones
  // management/membre) reste inchangé par la bascule vers /client/:clientId.
  selectClient(client: IClientRoleAssignment): void {
    this.isSpaceMenuOpen.set(false);
    this.router.navigate(['/', RoutePaths.Client, client.ClientId]);
  }

  // État actif du sélecteur : dérivé de la zone AFFICHÉE (URL courante), jamais d'un repli sur
  // AuthStore.activeSpaceId — voir core/displayed-zone.ts.
  isSpaceActive(space: ISpaceRoleAssignment): boolean {
    const zone = this.displayedZoneStore.zone();
    return (
      (zone.kind === 'management' && zone.spaceId === space.SpaceId) ||
      (zone.kind === 'member' && this.authStore.activeSpaceId() === space.SpaceId)
    );
  }

  isClientActive(client: IClientRoleAssignment): boolean {
    const zone = this.displayedZoneStore.zone();
    return zone.kind === 'client' && zone.clientId === client.ClientId;
  }

  logout(): void {
    this.isUserMenuOpen.set(false);
    this.authService.logout({
      RefreshToken: this.storage.GetRefreshToken() ?? undefined,
      DeviceId: this.storage.GetDeviceId() ?? undefined
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => this.router.navigate([`/${RoutePaths.Login}`]),
      error: () => {
        this.authStore.clear();
        this.router.navigate([`/${RoutePaths.Login}`]);
      }
    });
  }
}
