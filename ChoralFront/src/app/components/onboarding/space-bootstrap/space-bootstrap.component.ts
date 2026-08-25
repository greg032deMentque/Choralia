import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, tap, throwError } from 'rxjs';
import { managementPath, RoutePaths } from '@core/route-paths';
import { JoinCodePanelComponent } from '@app/components/onboarding/join-code-panel/join-code-panel.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { SpaceTypeEnum } from '@app/enums/space-type.enum';
import { ChoirMembersService } from '@app/services/members/choir-members.service';
import { ToastService } from '@app/services/toast.service';
import { IMemberChoir } from '@models/members-models/member-choir.model';

// Écran d'amorçage affiché juste après la création d'une chorale ou d'un événement : inviter
// par email + code de rattachement.
//
// - Chorale (spaceType === Chorale) : POST /api/choir-members/Invite existe réellement
//   (ChoirMembersController.Invite, policy ChoirManager) — appel HTTP réel, correction
//   ciblée de l'écart signalé au lot 6 (mailto: seul). Le body { ChoirId } transmis ici DOIT
//   correspondre à l'espace actif du store (X-Space-Id posé par TokenInterceptor) : c'est
//   garanti par OnboardingService.createChoir(), qui rafraîchit la session et positionne
//   l'espace actif (AuthStore.setActiveSpace) AVANT d'émettre le résultat consommé par
//   CreateChoirComponent (lequel ne rend cet écran qu'après cette émission) — ne pas dupliquer
//   cette logique ici, et ne jamais construire ce body à partir d'autre chose que l'input
//   `spaceId` reçu du parent.
// - Événement (spaceType === Event) : AUCUN endpoint d'invitation nominative n'a été
//   validé dans cette correction. Le contrat existant côté back (POST
//   /api/event-participants/Invite, EventParticipantController) attend un EventId,
//   pas un ChoirId — appeler l'endpoint chorale avec l'id d'un événement enverrait
//   l'invitation vers le mauvais espace (voire un espace inexistant). Écart assumé, conservé en
//   mailto: (comportement inchangé) en attendant une correction ciblée dédiée au flux événement.
@Component({
  selector: 'app-space-bootstrap',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    JoinCodePanelComponent,
    IconComponent,
    FormFieldComponent,
    SubmitOnceDirective
  ],
  templateUrl: './space-bootstrap.component.html',
  styleUrl: './space-bootstrap.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SpaceBootstrapComponent {
  readonly spaceId = input.required<string>();
  readonly spaceName = input.required<string>();
  readonly spaceType = input.required<SpaceTypeEnum>();

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly RoutePaths = RoutePaths;
  protected readonly SpaceTypeEnum = SpaceTypeEnum;

  private readonly fb = inject(FormBuilder);
  private readonly choirMembersService = inject(ChoirMembersService);
  private readonly toastService = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly dashboardLink = computed(() => managementPath(this.spaceId(), RoutePaths.Dashboard));

  readonly inviteForm = this.fb.nonNullable.group({
    email: this.fb.nonNullable.control('', [Validators.required, Validators.email]),
    firstname: this.fb.nonNullable.control(''),
    lastname: this.fb.nonNullable.control('')
  });

  // SubmitOnceDirective reste désactivée après un succès (garde anti-double-soumission) : sans
  // ce reset() explicite, vider le formulaire ne servait à rien — la deuxième invitation était
  // impossible sans recharger la page. Absente du DOM sur la branche Event (mailto:), d'où le `?`.
  private readonly inviteButton = viewChild<SubmitOnceDirective>('inviteButton');

  // Uniquement utilisé par la branche Event (mailto:, voir commentaire de classe).
  readonly mailtoHref = signal<string | null>(null);

  // Bouton sous appSubmitOnce (chorale uniquement) : rejet synchrone si formulaire invalide
  // (jamais d'appel HTTP dans ce cas). Succès -> toast + formulaire vidé pour permettre
  // d'enchaîner plusieurs invitations (cas d'consommation réel : on invite son pupitre d'un coup).
  // Échec HTTP -> aucun toast ici, ApiErrorInterceptor s'en charge déjà (Composant = état
  // inline uniquement) ; SubmitOnceDirective réactive le bouton automatiquement sur erreur.
  submitInviteChoir = (): Observable<IMemberChoir> => {
    if (this.inviteForm.invalid) {
      this.inviteForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    const { email, firstname, lastname } = this.inviteForm.getRawValue();

    return this.choirMembersService
      .invite({
        ChoirId: this.spaceId(),
        Email: email,
        Firstname: firstname.trim() || undefined,
        Lastname: lastname.trim() || undefined
      })
      .pipe(
        tap(() => {
          this.toastService.success(`Invitation envoyée à ${email}.`);
          this.inviteForm.reset({ email: '', firstname: '', lastname: '' });
          this.inviteButton()?.reset();
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };

  sendInvitationEvent(): void {
    const emailControl = this.inviteForm.controls.email;
    if (emailControl.invalid) {
      emailControl.markAsTouched();
      return;
    }

    const email = emailControl.value;
    const subject = encodeURIComponent(`Invitation à rejoindre ${this.spaceName()} sur Choralia`);
    const body = encodeURIComponent(
      `Bonjour,\n\nVous êtes invité(e) à rejoindre "${this.spaceName()}" sur Choralia.\n` +
        'Demandez le code de rattachement à votre responsable pour finaliser votre inscription.\n\nÀ bientôt !'
    );
    this.mailtoHref.set(`mailto:${encodeURIComponent(email)}?subject=${subject}&body=${body}`);
  }
}
