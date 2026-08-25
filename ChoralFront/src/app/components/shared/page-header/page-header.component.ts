import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface IBreadcrumbItem {
  readonly label: string;
  readonly link?: string;
}

// Titre + fil d'Ariane + zone d'actions projetée (ng-contenu). Purement présentationnel
// (pas de test — voir CLAUDE.md ChoralFront, "à ne pas tester").
@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly breadcrumb = input<IBreadcrumbItem[]>([]);
}
