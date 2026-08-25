import { AbstractControl, ValidationErrors } from '@angular/forms';

// Politique de mot de passe ASP.NET Identity appliquée côté back (majuscule, minuscule,
// chiffre, caractère spécial) — la longueur minimale reste portée par Validators.minLength.
// Partagé par les DEUX écrans qui posent un mot de passe (reset-password, activate-account) :
// une copie par écran divergerait du back au premier changement de politique, et l'utilisateur
// verrait un formulaire accepté côté front puis refusé en 400.
export const PASSWORD_COMPLEXITY_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).+$/;

// Aligné sur [MinLength(8)] des ViewModels ResetPasswordRequestViewModel et
// ActivateAccountViewModel (back).
export const PASSWORD_MIN_LENGTH = 8;

// Validateur de groupe : suppose deux contrôles nommés `newPassword` et `confirmPassword`
// dans le FormGroup sur lequel il est posé. Tout écran qui le réutilise doit donc conserver
// ces deux noms de contrôles — c'est le contrat de ce validateur.
export function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const password = control.get('newPassword')?.value;
  const confirm = control.get('confirmPassword')?.value;
  return password === confirm ? null : { passwordsMismatch: true };
}
