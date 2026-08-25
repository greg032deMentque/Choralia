import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Composant partagé d'état de liste : chargement / erreur / vide. Pattern skeleton pour
// le chargement, message inline actionnable pour l'erreur (Spec §6.1, §6.2, §6.3).
@Component({
  selector: 'app-data-state',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './data-state.component.html',
  styleUrl: './data-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DataStateComponent {
  readonly loading = input<boolean>(false);
  readonly error = input<string | null>(null);
  readonly empty = input<boolean>(false);
  readonly emptyMessage = input<string>('Aucun élément pour le moment.');
  // Illustration de l'état vide (Spec §6.2) : l'icône du domaine — des notes pour un
  // répertoire vide — plutôt que la loupe générique, qui suggère à tort une recherche
  // infructueuse alors que la liste n'a jamais rien contenu.
  readonly emptyIcon = input<IconNameEnum>(IconNameEnum.MagnifyingGlass);
  // Action principale proposée depuis l'état vide, quand l'utilisateur a le droit de créer.
  // Sans libellé, aucun bouton n'est rendu.
  readonly emptyActionLabel = input<string | null>(null);
  readonly retryLabel = input<string | null>(null);
  readonly skeletonRows = input<number>(3);
  // 'skeleton' (défaut, lists) : comportement historique inchangé.
  // 'spinner' (fiches/formulaires) : indicateur centré.
  // 'overlay' (rechargement) : voile bloquant au-dessus du contenu déjà affiché.
  readonly variant = input<'skeleton' | 'spinner' | 'overlay'>('skeleton');

  readonly emptyAction = output();
  readonly retry = output();

  protected readonly IconNameEnum = IconNameEnum;

  protected readonly skeletonArray = computed(() => Array.from({ length: this.skeletonRows() }));

  onEmptyAction(): void {
    this.emptyAction.emit();
  }

  onRetry(): void {
    this.retry.emit();
  }
}
