import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmModalComponent } from '@app/components/shared/confirm-modal/confirm-modal.component';
import { ConfirmService } from '@app/services/confirm.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, ConfirmModalComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  // Point de montage unique de ConfirmService.confirm() — voir confirm.service.ts. Placé à la
  // racine (au-dessus du router-outlet) pour rester valable dans les 4 zones sans dupliquer
  // le montage dans chaque shell de zone.
  protected readonly confirmService = inject(ConfirmService);
}
