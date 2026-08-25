import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, catchError, from, switchMap, tap, throwError } from 'rxjs';
import { AdminUserService } from '@app/services/admin/admin-user.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { RoutePaths } from '@core/route-paths';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { IAdminUserDetail } from '@models/admin-models/admin-user-detail.model';
import { getMemberStatusLabel } from '@app/enums/member-status.enum';
import { getVoicePartLabel } from '@app/enums/voice-part.enum';
import { getPresenceLabel } from '@app/enums/presence.enum';
import { getUserRoleLabel, getUserRolesLabel } from '@app/enums/user-role.enum';

// Fiche agrégée d'un utilisateur — DÉDUPLIQUE tous ses rattachements (chorales, événements,
// clients) sous un même compte. TOUTES les actions de management vivent ici, jamais sur une ligne
// de user-list.component.ts (décision produit : une action sur une ligne de tableau
// suggérerait qu'on agit sur le rattachement plutôt que sur le compte entier).
//
// L'id de route est un userId (pas un id de rattachement) — validé (non vide) avant tout appel
// HTTP (OWASP A01). Un repli explicite (chaîne non vide) est utilisé plutôt qu'une regex UUID
// stricte : l'identifiant ASP.NET Identity (User.Id) n'est pas garanti être un GUID canonique
// par ce contrat (AdminUserDetailViewModel.Id est un `string` nu côté back).
@Component({
  selector: 'app-user-detail',
  standalone: true,
  imports: [RouterLink, DatePipe, ReactiveFormsModule, DataStateComponent, FormFieldComponent, SubmitOnceDirective, IconComponent],
  templateUrl: './user-detail.component.html',
  styleUrl: './user-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class UserDetailComponent {
  private readonly adminUserService = inject(AdminUserService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  // Lié via withComponentInputBinding() (app.config.ts) — jamais de paramMap.get() nu.
  readonly id = input<string | undefined>(undefined);

  protected readonly RoutePaths = RoutePaths;
  protected readonly IconNameEnum = IconNameEnum;
  protected readonly getMemberStatusLabel = getMemberStatusLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getPresenceLabel = getPresenceLabel;
  protected readonly getUserRoleLabel = getUserRoleLabel;
  protected readonly getUserRolesLabel = getUserRolesLabel;

  readonly detail = signal<IAdminUserDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly editingIdentity = signal(false);

  readonly form = this.fb.nonNullable.group({
    firstname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    lastname: this.fb.nonNullable.control('', [Validators.required, Validators.maxLength(100)]),
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email, Validators.maxLength(256)])
  });

  // Impacts chiffrés annoncés AVANT suppression (ConfirmService) — lus depuis la fiche déjà
  // chargée, jamais recalculés côté serveur au moment de la confirmation.
  readonly impactsSuppression = computed(() => {
    const current = this.detail();
    if (!current) return [];
    return [
      `${current.Choirs.length} rattachement(s) choir`,
      `${current.Events.length} participation(s) événement`,
      `${current.ClientAttachments.length} rattachement(s) client`
    ];
  });

  constructor() {
    // `id` est un signal input, non peuplé à la construction (voir song-detail/event-detail) —
    // load() gère déjà l'id absent/invalide.
    effect(() => {
      this.load();
    });
  }

  startEditIdentity(): void {
    const current = this.detail();
    if (!current) return;
    this.form.reset({ firstname: current.Firstname, lastname: current.Lastname, email: current.Email });
    this.editingIdentity.set(true);
  }

  cancelEditIdentity(): void {
    this.editingIdentity.set(false);
  }

  // Bouton sous appSubmitOnce (comme les autres actions) : une validation locale invalide
  // rejette immédiatement (throwError) sans appel HTTP — le back reste la seule source de
  // vérité pour le conflit d'email (409), traduit en message inline (pas de code brut).
  submitIdentity = (): Observable<IAdminUserDetail> => {
    const current = this.detail();
    if (this.form.invalid || !current) {
      this.form.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    this.error.set(null);
    const raw = this.form.getRawValue();

    return this.adminUserService
      .updateIdentity({ Id: current.Id, Firstname: raw.firstname, Lastname: raw.lastname, Email: raw.email })
      .pipe(
        tap(updated => {
          this.detail.set(updated);
          this.editingIdentity.set(false);
          this.toastService.success('Identité mise à jour.');
        }),
        catchError((err: unknown) => {
          if (err instanceof HttpErrorResponse && err.status === 409) {
            this.error.set('Cette adresse e-mail est déjà utilisée par un autre compte.');
          } else if (!(err instanceof Error && err.message === 'validation')) {
            this.error.set("Impossible d'enregistrer les modifications. Merci de réessayer.");
          }
          return throwError(() => err);
        })
      );
  };

  toggleActive = (): Observable<IAdminUserDetail> => {
    const current = this.detail();
    if (!current) return throwError(() => new Error('no-detail'));

    this.error.set(null);
    const nextActive = !current.IsActive;

    return this.adminUserService.setActive({ UserId: current.Id, IsActive: nextActive }).pipe(
      tap(updated => {
        this.detail.set(updated);
        this.toastService.success(nextActive ? 'Compte réactivé.' : 'Compte désactivé.');
      }),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 403) {
          this.error.set('Vous ne pouvez pas mettre à jour le statut de votre propre compte.');
        } else {
          this.error.set('Impossible de mettre à jour le statut de ce compte. Merci de réessayer.');
        }
        return throwError(() => err);
      })
    );
  };

  resetPasswordAction = (): Observable<unknown> => {
    const current = this.detail();
    if (!current) return throwError(() => new Error('no-detail'));

    this.error.set(null);

    return this.adminUserService.resetPassword(current.Id).pipe(
      tap(() => this.toastService.success('E-mail de réinitialisation envoyé.')),
      catchError((err: unknown) => {
        this.error.set('Impossible de réinitialiser le mot de passe de ce compte. Merci de réessayer.');
        return throwError(() => err);
      })
    );
  };

  resendInvitationAction = (): Observable<unknown> => {
    const current = this.detail();
    if (!current) return throwError(() => new Error('no-detail'));

    this.error.set(null);

    return this.adminUserService.resendInvitation(current.Id).pipe(
      tap(() => this.toastService.success('Invitation renvoyée.')),
      catchError((err: unknown) => {
        if (err instanceof HttpErrorResponse && err.status === 409) {
          this.error.set("Ce compte n'est plus en attente d'invitation.");
        } else {
          this.error.set("Impossible de renvoyer l'invitation. Merci de réessayer.");
        }
        return throwError(() => err);
      })
    );
  };

  // Confirmation (ConfirmService, danger: true, impacts chiffrés) intégrée dans le flux
  // observable lui-même : "annulé" rejette sans jamais appeler le back (vérifiable côté test),
  // "confirmé" enchaîne l'appel DELETE réel. Un rejet d'annulation ré-actif le bouton
  // (comportement de SubmitOnceDirective en cas d'erreur) sans afficher de message — ce n'est
  // pas une erreur, juste un choix de l'utilisateur.
  deleteAction = (): Observable<unknown> => {
    const current = this.detail();
    if (!current) return throwError(() => new Error('no-detail'));

    return from(
      this.confirmService.confirm({
        title: 'Supprimer ce compte ?',
        message: `Cette action supprimera définitivement le compte de ${current.Firstname} ${current.Lastname}.`,
        impacts: this.impactsSuppression(),
        danger: true,
        confirmationLabel: 'Supprimer'
      })
    ).pipe(
      switchMap(confirmed => {
        if (!confirmed) return throwError(() => new Error('cancelled'));
        this.error.set(null);
        return this.adminUserService.delete(current.Id);
      }),
      tap(() => {
        this.toastService.success('Compte supprimé.');
        this.router.navigate(['/', RoutePaths.Admin, RoutePaths.AdminUsers]);
      }),
      catchError((err: unknown) => {
        if (err instanceof Error && err.message === 'cancelled') {
          return throwError(() => err);
        }
        if (err instanceof HttpErrorResponse && err.status === 403) {
          this.error.set('Vous ne pouvez pas supprimer votre propre compte.');
        } else if (err instanceof HttpErrorResponse && err.status === 409) {
          this.error.set('Impossible de supprimer le dernier administrateur.');
        } else {
          this.error.set('Impossible de supprimer ce compte. Merci de réessayer.');
        }
        return throwError(() => err);
      }),
      takeUntilDestroyed(this.destroyRef)
    );
  };

  private load(): void {
    const userId = this.id();
    if (!userId || userId.trim().length === 0) {
      this.loading.set(false);
      this.error.set('Identifiant utilisateur invalide.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    this.adminUserService
      .getUserDetail(userId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: detail => {
          this.detail.set(detail);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger cet utilisateur.');
        }
      });
  }
}
