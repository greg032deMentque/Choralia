import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  Injector,
  afterNextRender,
  inject,
  signal
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { SidebarComponent } from '@app/components/layout/sidebar/sidebar.component';
import { TopbarComponent } from '@app/components/layout/topbar/topbar.component';

// Deux états de navigation distincts, à ne pas confondre :
//   - `sidebarCollapsed` : à partir de 1024 px, la barre latérale se réduit aux icônes. Choix
//     durable de l'utilisateur, il survit à la navigation.
//   - `mobileNavOpen` : en dessous de 1024 px, la barre latérale devient un tiroir superposé.
//     État transitoire, refermé à chaque navigation — sinon le tiroir masque la page qu'on
//     vient d'ouvrir.
@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent, TopbarComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ShellComponent {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly elementRef = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);

  readonly sidebarCollapsed = signal(false);
  readonly mobileNavOpen = signal(false);

  constructor() {
    this.router.events
      .pipe(
        filter(event => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(() => this.closeMobileNav());
  }

  @HostListener('document:keydown.escape')
  onEscapeKeydown(): void {
    this.closeMobileNav();
  }

  toggleSidebar(): void {
    this.sidebarCollapsed.update(collapsed => !collapsed);
  }

  toggleMobileNav(): void {
    if (this.mobileNavOpen()) {
      this.closeMobileNav();
      return;
    }

    this.mobileNavOpen.set(true);
    this.focusAfterRender('.sidebar__close-btn', () => this.mobileNavOpen());
  }

  closeMobileNav(): void {
    const shouldRestoreFocus = this.mobileNavOpen();
    this.mobileNavOpen.set(false);
    if (shouldRestoreFocus) {
      this.focusAfterRender('.topbar__nav-toggle', () => !this.mobileNavOpen());
    }
  }

  private focusAfterRender(selector: string, shouldFocus: () => boolean): void {
    afterNextRender(
      () => {
        if (shouldFocus()) {
          (this.elementRef.nativeElement.querySelector(selector) as HTMLButtonElement | null)?.focus();
        }
      },
      { injector: this.injector }
    );
  }
}
