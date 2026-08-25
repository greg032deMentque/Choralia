import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@app/services/auth/auth.service';
import { RoutePaths } from '@core/route-paths';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ForgotPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly submitted = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email])
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.isSubmitting.set(true);

    this.authService.forgotPassword(this.form.getRawValue().email).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set("Impossible d'envoyer l'email pour le moment. Réessayez plus tard.");
      }
    });
  }
}
