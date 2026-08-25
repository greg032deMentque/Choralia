import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { IAuthenticatedUser } from '@models/auth-models/authenticated-user.model';
import { ISpaceRoleAssignment } from '@models/auth-models/space-role-assignment.model';
import { IClientRoleAssignment } from '@models/auth-models/client-role-assignment.model';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { UserRoleEnum, userRoleFromString } from '@app/enums/user-role.enum';
import { StorageService } from '@app/services/storage.service';
import { IResolvedZone, resolveZone } from '@core/zone-resolver';

// AuthStore expose uniquement les claims nécessaires à l'UI. Le JWT brut n'est jamais
// stocké dans un signal public — il reste uniquement dans StorageService (sessionStorage).
//
// Rôles scopés par espace (ChoralFront/CLAUDE.md — Rôles, chorale actif et guards, étendu au
// lot 4 zones) : pas de notion de "chorale actif" côté serveur, le front sélectionne un
// espace dans SpaceRoles et transmet son SpaceId via le header X-Space-Id (posé par
// TokenInterceptor). Seul SpaceRoles fait foi : l'ancien ChoirRoles, qui n'en etait qu'un
// sous-ensemble filtre sur SpaceType===Chorale, a ete retire du contrat.
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly storage = inject(StorageService);

  private readonly userSignal = signal<IAuthenticatedUser | null>(null);
  private readonly activeSpaceIdSignal = signal<string | null>(null);

  readonly user: Signal<IAuthenticatedUser | null> = this.userSignal.asReadonly();
  readonly activeSpaceId: Signal<string | null> = this.activeSpaceIdSignal.asReadonly();

  readonly isAuthenticated = computed(() => this.userSignal() !== null);

  readonly isGlobalAdmin = computed(() => this.userSignal()?.Roles.includes('Admin') ?? false);

  readonly spaceRoles = computed<ISpaceRoleAssignment[]>(() => this.userSignal()?.SpaceRoles ?? []);
  readonly clientRoles = computed<IClientRoleAssignment[]>(() => this.userSignal()?.ClientRoles ?? []);

  readonly activeSpace = computed<ISpaceRoleAssignment | null>(() => {
    const spaceId = this.activeSpaceIdSignal();
    if (!spaceId) return null;
    return this.spaceRoles().find(e => e.SpaceId === spaceId) ?? null;
  });

  readonly activeSpaceType = computed<SpaceTypeEnum | null>(() => this.activeSpace()?.SpaceType ?? null);

  readonly activeSpaceRoles = computed<UserRoleEnum[]>(() => {
    const assignment = this.activeSpace();
    if (!assignment) return [];
    return assignment.Roles
      .map(role => userRoleFromString(role))
      .filter((role): role is UserRoleEnum => role !== null);
  });

  // Dérive la zone du couple (utilisateur, espace actif) — jamais une propriété isolée de
  // l'utilisateur seul. Changer d'espace actif (setActiveSpace) peut donc change de zone
  // (ex. bascule /management -> /moi). Voir core/zone-resolver.ts pour la règle de priorité.
  readonly currentZone = computed<IResolvedZone>(() => resolveZone(this.userSignal(), this.activeSpaceIdSignal()));

  constructor() {
    this.rehydrate();
  }

  setSession(token: { AccessToken: string | null; RefreshToken: string | null }): void {
    this.storage.SetToken(token.AccessToken);
    this.storage.SetRefreshToken(token.RefreshToken);
  }

  setCurrentUser(user: IAuthenticatedUser): void {
    this.userSignal.set(user);
    const storedSpaceId = this.storage.GetActiveSpaceId();
    const stillValid = storedSpaceId !== null && user.SpaceRoles.some(e => e.SpaceId === storedSpaceId);
    if (stillValid) {
      this.activeSpaceIdSignal.set(storedSpaceId);
    } else if (user.SpaceRoles.length > 0) {
      this.setActiveSpace(user.SpaceRoles[0].SpaceId);
    } else {
      // 0 rattachement : ne jamais laisser un espace actif fantôme (bug latent avant ce lot —
      // aucun espace actif posé ici laissait aussi partir tout appel scopé sans X-Space-Id).
      // currentZone() renvoie 'no-space' dans ce cas — écran dédié, jamais une page
      // blanche ni une boucle de redirection.
      this.activeSpaceIdSignal.set(null);
      this.storage.SetActiveSpaceId(null);
    }
  }

  setActiveSpace(spaceId: string): void {
    this.activeSpaceIdSignal.set(spaceId);
    this.storage.SetActiveSpaceId(spaceId);
  }

  clear(): void {
    this.userSignal.set(null);
    this.activeSpaceIdSignal.set(null);
    this.storage.Clear();
  }

  private rehydrate(): void {
    const spaceId = this.storage.GetActiveSpaceId();
    if (spaceId) {
      this.activeSpaceIdSignal.set(spaceId);
    }
    // Le profil complet (userSignal) est repeuplé par AuthService.initializeSession()
    // via GET /api/auth/Me au démarrage de l'app (provideAppInitializer), pas ici :
    // le store seul n'a pas accès à HttpClient pour rester dépourvu de logique HTTP.
  }
}
