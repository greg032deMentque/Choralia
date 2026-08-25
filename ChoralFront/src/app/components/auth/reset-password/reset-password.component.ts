import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@app/services/auth/auth.service';
import { RoutePaths } from '@core/route-paths';
import { PASSWORD_COMPLEXITY_PATTERN, PASSWORD_MIN_LENGTH, passwordsMatchValidator } from '@core/password.validators';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Query params liés via withComponentInputBinding() (app.config.ts) : jamais de
// paramMap.get() nu. userId/token sont validés avant tout appel HTTP (OWASP A01).
@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ResetPasswordComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly userId = input<string | undefined>(undefined);
  readonly token = input<string | undefined>(undefined);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly submitted = signal(false);

  readonly hasValidParams = computed(() => Boolean(this.userId()) && Boolean(this.token()));

  readonly form = this.fb.nonNullable.group(
    {
      newPassword: this.fb.nonNullable.control('', [
        Validators.required,
        Validators.minLength(PASSWORD_MIN_LENGTH),
        Validators.pattern(PASSWORD_COMPLEXITY_PATTERN)
      ]),
      confirmPassword: this.fb.nonNullable.control('', [Validators.required])
    },
    { validators: passwordsMatchValidator }
  );

  submit(): void {
    const userId = this.userId();
    const token = this.token();
    if (!userId || !token) {
      this.error.set('Lien de réinitialisation invalide ou incomplet.');
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.isSubmitting.set(true);

    this.authService.resetPassword({
      UserId: userId,
      Token: token,
      NewPassword: this.form.getRawValue().newPassword
    }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.submitted.set(true);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set('Le lien a expiré ou est invalide. Merci de refaire une demande.');
      }
    });
  }

  goToLogin(): void {
    this.router.navigate([`/${RoutePaths.Login}`]);
  }
}
