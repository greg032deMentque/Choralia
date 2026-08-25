import { Injectable, inject } from '@angular/core';
import { ToastrService, IndividualConfig } from 'ngx-toastr';
import { UndoToastComponent } from '@app/components/shared/undo-toast/undo-toast.component';

// Durées imposées (ChoralFront/CLAUDE.md) : succès 3s, warning 5s, erreur persistante
// jusqu'à dismiss, max 3 toasts empilés (config globale : provideToastr en app.config.ts).
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly toastr = inject(ToastrService);

  success(message: string, title?: string): void {
    this.toastr.success(message, title, { timeOut: 3000 });
  }

  warning(message: string, title?: string): void {
    this.toastr.warning(message, title, { timeOut: 5000 });
  }

  // closeButton indispensable : « persistant jusqu'à dismiss manuel » (11-ux-ui §Toast) exige
  // une affordance visible. Sans croix, seul un clic dans le corps du toast le ferme — non
  // découvrable, et l'utilisateur croit le toast bloqué.
  error(message: string, title?: string): void {
    this.toastr.error(message, title, { disableTimeOut: true, tapToDismiss: true, closeButton: true });
  }

  info(message: string, title?: string): void {
    this.toastr.info(message, title, { timeOut: 3000 });
  }

  // Action réversible : au lieu d'une modale de confirmation AVANT, l'action est exécutée
  // immédiatement et le toast porte son annulation (Spec §6.4, décision `10-D42`). Réservé
  // aux actions dont l'inverse existe réellement côté API — sans quoi le bouton mentirait.
  // Durée 5 s : le temps de lire et de réagir, sans immobiliser le coin de l'écran.
  undoable(message: string, undoLabel: string, onUndo: () => void): void {
    const toast = this.toastr.success(message, undefined, {
      timeOut: 5000,
      tapToDismiss: false,
      closeButton: false,
      toastComponent: UndoToastComponent,
      payload: { undoLabel }
    } as Partial<IndividualConfig<{ undoLabel: string }>>);

    // `onAction` n'est émis que par le bouton « Annuler » d'UndoToastComponent. L'expiration
    // du délai, comme un clic ailleurs dans le toast, vaut acceptation — jamais annulation :
    // brancher l'inverse sur toute la surface faisait annuler l'action à qui voulait fermer.
    toast.onAction.subscribe(() => onUndo());
  }
}
