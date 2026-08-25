import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { RoutePaths } from '@core/route-paths';

@Component({
  selector: 'app-not-found',
  standalone: true,
  imports: [IconComponent],
  templateUrl: './not-found.component.html',
  styleUrl: './not-found.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NotFoundComponent {
  private readonly router = inject(Router);

  protected readonly IconNameEnum = IconNameEnum;

  protected goToDashboard(): void {
    this.router.navigate([`/${RoutePaths.Dashboard}`]);
  }
}
