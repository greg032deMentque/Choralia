import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { ModalComponent } from '@app/components/shared/modal/modal.component';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { IMembershipRequestListItem } from '@models/onboarding-models/membership-request-list-item.model';
import { IApproveRequestRequest } from '@models/onboarding-models/approve-request-request.model';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { UserRoleEnum, getUserRoleLabel } from '@app/enums/user-role.enum';

const ELIGIBLE_ROLES: UserRoleEnum[] = [UserRoleEnum.Singer, UserRoleEnum.Manager];
const AVAILABLE_VOICE_PARTS: VoicePartEnum[] = [VoicePartEnum.Alto, VoicePartEnum.Soprano, VoicePartEnum.Bass, VoicePartEnum.Tenor];

// Formulaire modal d'admission — voix principale ET rôle exigés dans la même opération (jamais
// une admission "nue" qui produirait un membre invalide, cf. AdmettreDemandeViewModel back).
// Composant purement formulaire : ne fait aucun appel HTTP lui-même, l'appel et la management du
// 409 (plafond atteint) restent portés par le parent (membership-requests-list.component), seul
// à connaître l'état de la liste entière (bandeau persistant).
@Component({
  selector: 'app-approve-request-modal',
  standalone: true,
  imports: [ModalComponent, ReactiveFormsModule],
  templateUrl: './approve-request-modal.component.html',
  styleUrl: './approve-request-modal.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ApproveRequestModalComponent {
  private readonly fb = inject(FormBuilder);

  readonly request = input.required<IMembershipRequestListItem>();
  readonly isSubmitting = input<boolean>(false);

  readonly confirmed = output<IApproveRequestRequest>();
  readonly cancelled = output();

  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly getUserRoleLabel = getUserRoleLabel;
  protected readonly voicePartDisponibles = AVAILABLE_VOICE_PARTS;
  protected readonly rolesAdmissibles = ELIGIBLE_ROLES;

  readonly form = this.fb.nonNullable.group({
    primaryVoicePart: this.fb.nonNullable.control<VoicePartEnum | null>(null, [Validators.required]),
    role: this.fb.nonNullable.control<UserRoleEnum | null>(UserRoleEnum.Singer, [Validators.required])
  });

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    if (raw.primaryVoicePart === null || raw.role === null) return;

    this.confirmed.emit({ PrimaryVoicePart: raw.primaryVoicePart, Role: raw.role });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
