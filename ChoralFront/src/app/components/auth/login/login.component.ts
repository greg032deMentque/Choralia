import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@app/services/auth/auth.service';
import { RoutePaths, isAllowedReturnUrl } from '@core/route-paths';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, IconComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly showPassword = signal(false);

  readonly form = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    password: this.fb.nonNullable.control('', [Validators.required]),
    rememberMe: this.fb.nonNullable.control(false)
  });

  togglePasswordVisibility(): void {
    this.showPassword.update(v => !v);
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.error.set(null);
    this.isSubmitting.set(true);

    const { email, password } = this.form.getRawValue();

    this.authService.login({ Email: email, Password: password }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        const requestedReturnUrl = new URLSearchParams(window.location.search).get('returnUrl');
        const target = requestedReturnUrl && isAllowedReturnUrl(requestedReturnUrl)
          ? requestedReturnUrl
          : `/${RoutePaths.Dashboard}`;
        this.router.navigateByUrl(target);
      },
      error: () => {
        this.isSubmitting.set(false);
        this.error.set('Email ou mot de passe incorrect.');
      }
    });
  }
}
