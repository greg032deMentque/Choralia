import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

// Barre de pagination unique. Le bloc était recopié à l'identique sur six écrans, et cinq des
// six copies n'affichaient ni le nombre total de résultats ni de sélecteur de taille de page —
// or le total est l'information que cherchent le responsable et l'administrateur.
@Component({
  selector: 'app-pagination',
  standalone: true,
  template: `
    @if (totalPages() > 1 || totalCount() !== null) {
      <nav class="pagination-bar" [attr.aria-label]="ariaLabel()">
        @if (totalCount() !== null) {
          <span class="pagination-bar__count">{{ totalCount() }} résultat{{ totalCount() === 1 ? '' : 's' }}</span>
        }

        @if (totalPages() > 1) {
          <ul class="pagination mb-0">
            <li class="page-item" [class.disabled]="page() === 1">
              <button type="button" class="page-link" [disabled]="page() === 1" (click)="pageChange.emit(page() - 1)">
                Précédent
              </button>
            </li>
            <li class="page-item disabled">
              <span class="page-link" aria-current="page">{{ page() }} / {{ totalPages() }}</span>
            </li>
            <li class="page-item" [class.disabled]="page() === totalPages()">
              <button type="button" class="page-link" [disabled]="page() === totalPages()" (click)="pageChange.emit(page() + 1)">
                Suivant
              </button>
            </li>
          </ul>
        }
      </nav>
    }
  `,
  styleUrl: './pagination.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PaginationComponent {
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  // `null` = total inconnu ou volontairement masqué : la ligne de comptage disparaît alors.
  readonly totalCount = input<number | null>(null);
  readonly ariaLabel = input<string>('Pagination');

  readonly pageChange = output<number>();
}
