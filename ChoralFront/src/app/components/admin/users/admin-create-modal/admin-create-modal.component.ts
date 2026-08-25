import { ChangeDetectionStrategy, Component, DestroyRef, inject, output, signal } from '@angular/core';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, tap, throwError } from 'rxjs';
import { AdminUserService } from '@app/services/admin/admin-user.service';
import { ToastService } from '@app/services/toast.service';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IAdminUserListItem } from '@models/admin-models/admin-user-list-item.model';

// Regex identique au back (CreateAdminUserViewModel, Chorale.ViewModels) : min 8 caractères,
// au moins une majuscule, une minuscule, un chiffre et un caractère spécial. Reprise ici pour
// un retour immédiat sans aller-retour serveur — le back reste la seule source de vérité.
const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z0-9]).+$/;

// Modale de création d'un compte administrateur (onglet "Administrateurs" de
// user-list.component.ts). Pas de bibliothèque de modale disponible (Angular Material/
// ng-bootstrap interdits, bootstrap.bundle.js — comportement JS des modales Bootstrap — non
// chargé dans ce projet, CSS uniquement) : recours à une marque HTML manuelle avec les classes
// utilitaires Bootstrap (.modal/.modal-backdrop), fermeture au clic sur le fond ou Échap.
@Component({
  selector: 'app-admin-create-modal',
  standalone: true,
  imports: [ModalComponent, ReactiveFormsModule, FormFieldComponent, SubmitOnceDirective],
  templateUrl: './admin-create-modal.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AdminCreateModalComponent {
  private readonly adminUserService = inject(AdminUserService);
  private readonly toastService = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly created = output<IAdminUserListItem>();
  readonly cancelled = output();

  readonly error = signal<string | null>(null);

  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.nonNullable.group({
    firstname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    lastname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)]),
    password: this.fb.nonNullable.control('', [Validators.required, Validators.minLength(8), Validators.pattern(PASSWORD_PATTERN)])
  });

  cancel(): void {
    this.cancelled.emit();
  }

  // Bouton sous appSubmitOnce : la validation invalide relève d'un rejet synchrone (le back
  // n'est jamais appelé), le succès émet `created` vers le parent qui referme la modale.
  submitAction = (): Observable<IAdminUserListItem> => {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.error.set(null);
    const raw = this.form.getRawValue();

    return this.adminUserService
      .createAdmin({ Email: raw.email, Firstname: raw.firstname, Lastname: raw.lastname, Password: raw.password })
      .pipe(
        tap(createdAdmin => {
          this.toastService.success('Administrateur créé.');
          this.created.emit(createdAdmin);
        }),
        catchError((err: unknown) => {
          if (!(err instanceof Error && err.message === 'validation')) {
            const message =
              err instanceof HttpErrorResponse && err.status === 409
                ? 'Cette adresse e-mail est déjà utilisée par un autre compte.'
                : "Impossible de créer cet administrateur. Merci de réessayer.";
            this.error.set(message);
          }
          return throwError(() => err);
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };
}
