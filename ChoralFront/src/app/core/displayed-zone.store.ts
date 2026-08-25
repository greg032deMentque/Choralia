import { Injectable, Signal, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { IDisplayedZone, resolveDisplayedZone } from '@core/displayed-zone';

// Wrapper Signal de displayed-zone.ts — seul point d'accès Angular à la zone AFFICHÉE. Se
// recalcule à chaque navigation terminée (NavigationEnd, urlAfterRedirects : tient compte des
// redirections de guards, contrairement à event.url) ; valeur initiale posée depuis router.url
// pour ne jamais démarrer sur 'no-space' avant la première navigation.
@Injectable({ providedIn: 'root' })
export class DisplayedZoneStore {
  private readonly router = inject(Router);

  readonly zone: Signal<IDisplayedZone> = toSignal(
    this.router.events.pipe(
      filter((event): event is NavigationEnd => event instanceof NavigationEnd),
      map(event => resolveDisplayedZone(event.urlAfterRedirects))
    ),
    { initialValue: resolveDisplayedZone(this.router.url) }
  );
}
