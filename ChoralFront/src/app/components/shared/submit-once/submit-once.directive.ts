import { Directive, DestroyRef, ElementRef, Renderer2, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, finalize } from 'rxjs';

// Action déclenchée par le bouton : invoquée au clic, doit retourner l'Observable de la
// requête (POST/DELETE) à surveiller pour savoir si l'action a réussi ou échoué.
export type SubmitOnceAction = () => Observable<unknown>;

// Directive anti-double-clic pour les boutons d'action à effet de bord (POST/DELETE).
// Désactive le bouton dès le déclenchement et affiche un spinner inline SANS faire
// disparaître le libellé du bouton (accessibilité — un bouton qui se vide de son texte
// pendant le chargement est un piège classique). Réactive le bouton en cas d'erreur : sinon
// un échec réseau bloque définitivement l'utilisateur, l'autre piège classique du pattern
// submit-once. Reste désactivé en cas de succès jusqu'à navigation ou reset() explicite
// (évite une seconde soumission accidentelle avant que la vue ne se referme/rafraîchisse).
@Directive({
  selector: '[appSubmitOnce]',
  standalone: true,
  exportAs: 'appSubmitOnce',
  host: {
    // [attr.disabled] plutôt que [disabled] : la directive est une sélection attribut
    // générique, Angular ne peut pas vérifier statiquement que l'hôte expose une propriété
    // DOM `disabled` (NG8002). L'attribut disabled est correctement reflété par le navigateur
    // (et par jsdom) sur la propriété .disabled des éléments de formulaire.
    '[attr.disabled]': "disabled() ? '' : null",
    '[attr.aria-busy]': "pending() ? 'true' : 'false'",
    '(click)': 'onClick()'
  }
})
export class SubmitOnceDirective {
  private readonly elementRef = inject<ElementRef<HTMLButtonElement>>(ElementRef);
  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);

  readonly appSubmitOnce = input<SubmitOnceAction | null>(null);

  readonly pending = signal(false);
  readonly disabled = signal(false);

  // Garde synchrone indépendante de la détection de changements : un double-clic peut
  // survenir avant qu'une passe de CD n'ait eu le temps de refléter `disabled` dans le DOM.
  private triggered = false;
  private spinnerEl: HTMLElement | null = null;

  onClick(): void {
    const action = this.appSubmitOnce();
    if (!action || this.triggered) return;

    this.triggered = true;
    this.pending.set(true);
    this.disabled.set(true);
    this.showSpinner();

    action()
      .pipe(
        finalize(() => {
          this.pending.set(false);
          this.hideSpinner();
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: () => {
          // Succès : reste désactivé jusqu'à reset() explicite ou navigation/destruction.
        },
        error: () => {
          this.triggered = false;
          this.disabled.set(false);
        }
      });
  }

  // Réinitialisation explicite (ex. réouverture d'un formulaire déjà soumis avec succès).
  reset(): void {
    this.triggered = false;
    this.pending.set(false);
    this.disabled.set(false);
    this.hideSpinner();
  }

  private showSpinner(): void {
    if (this.spinnerEl) return;
    const spinner = this.renderer.createElement('span') as HTMLElement;
    this.renderer.addClass(spinner, 'app-submit-once__spinner');
    this.renderer.setAttribute(spinner, 'aria-hidden', 'true');
    this.renderer.insertBefore(this.elementRef.nativeElement, spinner, this.elementRef.nativeElement.firstChild);
    this.spinnerEl = spinner;
  }

  private hideSpinner(): void {
    if (!this.spinnerEl) return;
    this.renderer.removeChild(this.elementRef.nativeElement, this.spinnerEl);
    this.spinnerEl = null;
  }
}
