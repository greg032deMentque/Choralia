import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Toast } from 'ngx-toastr';

// Toast d'action réversible (`10-D42`). Le corps du toast NE déclenche PAS l'annulation :
// seul le bouton le fait. L'implémentation précédente branchait l'inverse de l'action sur
// toute la surface du toast, si bien qu'un clic destiné à le fermer annulait l'action que
// l'utilisateur venait de demander — et rien n'indiquait que le texte était cliquable.
//
// Le bloc `host` ci-dessous n'est PAS décoratif : les liaisons d'hôte de ngx-toastr (classes
// du toast, affichage, survol qui suspend le compte à rebours, animation d'entrée) vivent sur
// le décorateur de `Toast`/`ToastBase` et Angular n'hérite pas les décorateurs. Sans cette
// recopie, le toast s'afficherait sans aucun style ni animation. Le composant n'étant instancié
// que dynamiquement par ngx-toastr, son sélecteur est libre.
@Component({
  selector: 'app-undo-toast',
  standalone: true,
  host: {
    '[class]': 'toastClasses()',
    '[style.display]': 'displayStyle()',
    '[style.--animation-easing]': 'params.easing',
    '[style.--animation-duration]': "params.easeTime + 'ms'",
    'animate.enter': 'toast-in',
    '(mouseenter)': 'stickAround()',
    '(mouseleave)': 'delayedHideToast()',
    '(click)': 'tapToast()'
  },
  template: `
    <div class="undo-toast">
      <span class="undo-toast__message">{{ message() }}</span>
      <button type="button" class="btn btn-sm btn-link undo-toast__action" (click)="undo($event)">
        {{ undoLabel }}
      </button>
      <button type="button" class="btn-close btn-close-white undo-toast__close" aria-label="Fermer" (click)="close($event)"></button>
    </div>
  `,
  styleUrl: './undo-toast.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UndoToastComponent extends Toast<{ undoLabel: string }> {
  protected get undoLabel(): string {
    return this.options().payload?.undoLabel ?? 'Annuler';
  }

  // `triggerAction` remonte au souscripteur d'`onAction` : c'est l'appelant qui porte l'inverse
  // de l'action, jamais ce composant, qui ne connaît pas le domaine.
  protected undo(event: Event): void {
    event.stopPropagation();
    this.toastPackage.triggerAction();
    this.remove();
  }

  protected close(event: Event): void {
    event.stopPropagation();
    this.remove();
  }
}
