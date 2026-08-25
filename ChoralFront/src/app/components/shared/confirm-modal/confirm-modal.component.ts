import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Rendu générique de `ConfirmService.confirm()` (voir confirm.service.ts) — un seul point de
// montage dans App, jamais instancié directement par une feature. `showCloseButton="false"`
// sur `ModalComponent` : sans lui le piège à focus poserait le focus initial sur le `×` du
// header plutôt que sur Annuler ou le champ de saisie (décision de migration ConfirmService,
// focus toujours sûr par défaut y compris en danger — cf. plan validé).
@Component({
  selector: 'app-confirm-modal',
  standalone: true,
  imports: [ModalComponent, IconComponent, ReactiveFormsModule],
  templateUrl: './confirm-modal.component.html',
  styleUrl: './confirm-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConfirmModalComponent {
  private readonly fb = inject(FormBuilder);

  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly impacts = input<string[]>([]);
  readonly confirmationKeyword = input<string | null>(null);
  readonly confirmationLabel = input<string>('Confirmer');
  readonly danger = input<boolean>(false);

  readonly confirmed = output();
  readonly cancelled = output();

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly keywordControl = this.fb.nonNullable.control('');

  private readonly keywordValue = toSignal(this.keywordControl.valueChanges, { initialValue: '' });

  // Seule règle de validation du champ — remplace le duo `didOpen`/`preConfirm` de l'ancienne
  // implémentation sweetalert2 : ici le bouton Confirmer réellement désactivé est le seul
  // chemin de soumission, un second filet de sécurité serait une duplication sans objet.
  protected readonly confirmDisabled = computed(() => {
    const keyword = this.confirmationKeyword();
    return keyword !== null && this.keywordValue() !== keyword;
  });

  protected confirm(): void {
    if (this.confirmDisabled()) return;
    this.confirmed.emit();
  }

  protected cancel(): void {
    this.cancelled.emit();
  }
}
