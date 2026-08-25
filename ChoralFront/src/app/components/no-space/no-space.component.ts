import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@app/services/auth/auth.service';
import { AuthStore } from '@core/auth.store';
import { StorageService } from '@app/services/storage.service';
import { RoutePaths } from '@core/route-paths';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Écran dédié pour un utilisateur authentifié mais sans AUCUN rattachement exploitable
// (ni claim Admin, ni SpaceRoles, ni ClientRoles) — zone-resolver.ts renvoie 'no-space'
// dans ce cas précis. Jamais une page blanche, jamais une boucle de redirection/403 : ici
// l'utilisateur peut uniquement se déconnecter (aucune action possible tant qu'un
// rattachement ne lui est pas accordé côté back).
@Component({
  selector: 'app-no-space',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './no-space.component.html',
  styleUrl: './no-space.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NoSpaceComponent {
  private readonly authService = inject(AuthService);
  private readonly authStore = inject(AuthStore);
  private readonly storage = inject(StorageService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly IconNameEnum = IconNameEnum;

  logout(): void {
    this.authService
      .logout({
        RefreshToken: this.storage.GetRefreshToken() ?? undefined,
        DeviceId: this.storage.GetDeviceId() ?? undefined
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.router.navigate([`/${RoutePaths.Login}`]),
        error: () => {
          this.authStore.clear();
          this.router.navigate([`/${RoutePaths.Login}`]);
        }
      });
  }
}
