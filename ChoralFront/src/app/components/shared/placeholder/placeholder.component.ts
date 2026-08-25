import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Écran temporaire pour les routes du squelette non encore implémentées en détail.
@Component({
  selector: 'app-placeholder',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './placeholder.component.html',
  styleUrl: './placeholder.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlaceholderComponent {
  readonly title = input.required<string>();

  protected readonly IconNameEnum = IconNameEnum;
}
