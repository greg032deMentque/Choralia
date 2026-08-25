import { Injectable, signal } from '@angular/core';

export interface IConfirmOptions {
  readonly title: string;
  readonly message: string;
  // Lignes chiffrées annonçant les conséquences AVANT l'action (ex. archivage, suspension).
  readonly impacts?: string[];
  // Quand fourni, l'utilisateur doit saisir exactement cette chaîne pour activer le bouton
  // de confirmation (casse comprise) — réservé aux actions à fort impact.
  readonly confirmationKeyword?: string;
  readonly confirmationLabel?: string;
  // Style destructif (bouton de confirmation en couleur danger).
  readonly danger?: boolean;
}

// Façade Promise<boolean> conservée à l'identique pour ne casser aucun appelant : le rendu
// réel est délégué à `ConfirmModalComponent`, monté une seule fois par `App` (voir app.html)
// et piloté par le signal `request` ci-dessous. `ConfirmService` ne connaît rien de Angular
// au-delà de `signal` — aucune dépendance vers `components/` (règle d'import services → *).
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private readonly requestSignal = signal<IConfirmOptions | null>(null);
  readonly request = this.requestSignal.asReadonly();

  private resolver: ((value: boolean) => void) | null = null;

  confirm(options: IConfirmOptions): Promise<boolean> {
    // Un confirm() déclenché pendant qu'un autre est en attente résoudrait sinon le premier
    // à `false` sans jamais le tenir informé si on l'écrasait tel quel — la Promise resterait
    // en attente indéfiniment. On la résout explicitement avant d'ouvrir la suivante.
    this.resolve(false);

    this.requestSignal.set(options);
    return new Promise<boolean>(resolve => {
      this.resolver = resolve;
    });
  }

  // Appelée uniquement par le point de montage global (App) sur (confirmed)/(cancelled).
  resolve(value: boolean): void {
    const resolver = this.resolver;
    this.resolver = null;
    this.requestSignal.set(null);
    resolver?.(value);
  }
}
