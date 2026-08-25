import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  input,
  output
} from '@angular/core';

let nextModalId = 0;

const FOCUSABLE =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Modale unique de l'application. Les quatre implémentations artisanales qu'elle remplace
// partageaient les mêmes manques (`11-ux-ui` §7.3) : aucune ne posait le focus à l'ouverture,
// aucune ne le piégeait, aucune ne bloquait le défilement du fond. La seule qui gérait Échap
// écoutait sur un `div[tabindex="-1"]` jamais focalisé — donc l'écouteur ne se déclenchait
// jamais. Ici l'écoute est posée sur le document, et le focus entre réellement dans la modale.
@Component({
  selector: 'app-modal',
  standalone: true,
  template: `
    <!-- Bouton natif plutot qu'un <div> : le fond est activable au clavier et annonce,
         sans avoir a recoller un gestionnaire de touches sur un element non interactif. -->
    <button type="button" class="modal-backdrop show app-modal__backdrop" aria-label="Fermer" (click)="dismiss()"></button>

    <div class="modal d-block app-modal" role="dialog" aria-modal="true" [attr.aria-labelledby]="titleId">
      <div class="modal-dialog" [class.modal-lg]="large()" [class.modal-fullscreen]="fullscreen()">
        <div class="modal-content" #panel>
          <div class="modal-header">
            <h2 class="modal-title h5" [id]="titleId">{{ title() }}</h2>
            @if (showCloseButton()) {
              <button type="button" class="btn-close" aria-label="Fermer" (click)="dismiss()"></button>
            }
          </div>
          <div class="modal-body">
            <ng-content />
          </div>
        </div>
      </div>
    </div>
  `,
  styleUrl: './modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ModalComponent implements AfterViewInit, OnDestroy {
  readonly title = input.required<string>();
  readonly large = input<boolean>(false);
  // Plein écran (`modal-fullscreen` de Bootstrap) : réservé aux contenus qu'une boîte de
  // dialogue étrangle — une partition PDF ou une image haute. Le piège à focus, le blocage du
  // défilement de fond et la fermeture par Échap restent ceux de cette modale : une visionneuse
  // qui se poserait en surcouche maison les réimplémenterait tous, moins bien.
  readonly fullscreen = input<boolean>(false);
  // Désactivé par les modales de confirmation (`ConfirmModalComponent`) : sans lui, le piège à
  // focus pose le focus initial sur le `×`, jamais sur Annuler/le champ de saisie — voir
  // `11-ux-ui` §7.3, décision de migration ConfirmService/sweetalert2.
  readonly showCloseButton = input<boolean>(true);
  readonly closed = output();

  protected readonly titleId = `app-modal-title-${++nextModalId}`;

  @ViewChild('panel') private readonly panel?: ElementRef<HTMLElement>;

  private previouslyFocused: HTMLElement | null = null;
  private readonly onKeydown = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.stopPropagation();
      this.dismiss();
      return;
    }
    if (event.key === 'Tab') this.trapFocus(event);
  };

  ngAfterViewInit(): void {
    // L'élément à re-focaliser est mémorisé AVANT tout déplacement : sans cela, fermer la
    // modale laisse le focus sur <body> et l'utilisateur au clavier repart du haut de la page.
    this.previouslyFocused = document.activeElement as HTMLElement | null;
    document.addEventListener('keydown', this.onKeydown, true);
    document.body.style.overflow = 'hidden';

    const panel = this.panel?.nativeElement;
    const first = panel?.querySelector<HTMLElement>(FOCUSABLE);
    (first ?? panel)?.focus();
  }

  ngOnDestroy(): void {
    document.removeEventListener('keydown', this.onKeydown, true);
    document.body.style.overflow = '';
    this.previouslyFocused?.focus();
  }

  protected dismiss(): void {
    this.closed.emit();
  }

  // Piège à focus : Tab depuis le dernier élément revient au premier, Maj+Tab depuis le
  // premier va au dernier. Sans lui, la tabulation sort de la modale et parcourt la page
  // masquée derrière — que le lecteur d'écran annonce alors qu'elle est inaccessible.
  private trapFocus(event: KeyboardEvent): void {
    const panel = this.panel?.nativeElement;
    if (!panel) return;

    const focusable = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
      element => element.offsetParent !== null
    );
    if (focusable.length === 0) return;

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    } else if (event.shiftKey && (active === first || active === panel)) {
      event.preventDefault();
      last.focus();
    }
  }
}
