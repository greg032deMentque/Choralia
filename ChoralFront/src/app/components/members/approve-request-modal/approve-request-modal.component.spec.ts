import { TestBed } from '@angular/core/testing';
import { ApproveRequestModalComponent } from './approve-request-modal.component';
import { MembershipRequestStatusEnum } from '@app/enums/status-membership-request.enum';
import { UserRoleEnum } from '@app/enums/user-role.enum';
import { IMembershipRequestListItem } from '@models/onboarding-models/membership-request-list-item.model';

function unRequest(): IMembershipRequestListItem {
  return {
    Id: 'demande-1',
    SpaceId: 'espace-1',
    UserId: 'user-1',
    UserFullName: 'Ada Lovelace',
    UserEmail: 'ada@example.com',
    Status: MembershipRequestStatusEnum.Pending,
    Message: null,
    DeclineReason: null,
    CreatedAt: '2026-07-01',
    HandledAt: null
  };
}

describe('AdmettreDemandeModalComponent', () => {
  it('sans voix principale sélectionnée : soumission bloquée, aucun événement confirmed émis', () => {
    const fixture = TestBed.createComponent(ApproveRequestModalComponent);
    fixture.componentRef.setInput('request', unRequest());
    fixture.detectChanges();

    const confirmedSpy = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmedSpy);

    // primaryVoicePart reste à null (valeur initiale) — role a une valeur par défaut (Chanteur)
    // mais le form est invalide tant que primaryVoicePart n'est pas choisi.
    fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(confirmedSpy).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.primaryVoicePart.touched).toBe(true);
  });

  it('avec voix et rôle renseignés : émet confirmed avec le payload attendu', () => {
    const fixture = TestBed.createComponent(ApproveRequestModalComponent);
    fixture.componentRef.setInput('request', unRequest());
    fixture.detectChanges();

    const confirmedSpy = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmedSpy);

    fixture.componentInstance.form.setValue({ primaryVoicePart: 1, role: UserRoleEnum.Singer });
    fixture.componentInstance.submit();

    expect(confirmedSpy).toHaveBeenCalledWith({ PrimaryVoicePart: 1, Role: UserRoleEnum.Singer });
  });
});
