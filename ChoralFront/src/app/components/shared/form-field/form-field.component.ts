import {
  AfterContentInit,
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  ContentChild,
  DoCheck,
  ElementRef,
  Renderer2,
  inject,
  input
} from '@angular/core';
import { NgControl, ValidationErrors } from '@angular/forms';

let nextFormFieldId = 0;

// Traduction centralisée des erreurs de validation Angular en messages français — SEUL
// endroit de l'application où cette table doit exister (voir CLAUDE.md ChoralFront).
export function getValidationErrorMessage(errors: ValidationErrors | null, fieldLabel: string): string | null {
  if (!errors) return null;
  if (errors['required']) return `${fieldLabel} est obligatoire.`;
  if (errors['email']) return 'Adresse e-mail invalide.';
  if (errors['minlength']) {
    return `${fieldLabel} doit contenir au moins ${errors['minlength'].requiredLength} caractères.`;
  }
  if (errors['maxlength']) {
    return `${fieldLabel} ne doit pas dépasser ${errors['maxlength'].requiredLength} caractères.`;
  }
  if (errors['pattern']) return `${fieldLabel} n'est pas au format attendu.`;
  if (errors['min']) return `${fieldLabel} doit être supérieur ou égal à ${errors['min'].min}.`;
  if (errors['max']) return `${fieldLabel} doit être inférieur ou égal à ${errors['max'].max}.`;
  return `${fieldLabel} est invalide.`;
}

// Enveloppe un champ de formulaire réactive projeté en ng-contenu : label + marqueur
// d'obligation, message d'erreur SOUS le champ (jamais en toast — un toast disparaît avant
// que l'utilisateur ait corrigé), message d'avertissement distinct, et câblage automatique
// de aria-describedby/aria-invalid sur l'élément projeté (via NgControl + ElementRef lus en
// ContentChild). ngDoCheck (et non un computed/effect signal) est nécessaire ici : les
// changements de `touched`/`invalid` d'un AbstractControl ne sont pas notifiés par un
// Observable synchrone (markAsTouched() n'émet pas sur statusChanges) — c'est exactement le
// pattern qu'utilise NgControlStatus en interne dans Angular.
@Component({
  selector: 'app-form-field',
  standalone: true,
  templateUrl: './form-field.component.html',
  styleUrl: './form-field.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FormFieldComponent implements AfterContentInit, DoCheck {
  private readonly renderer = inject(Renderer2);
  private readonly cdr = inject(ChangeDetectorRef);

  @ContentChild(NgControl) private readonly ngControl?: NgControl;
  @ContentChild(NgControl, { read: ElementRef }) private readonly controlElementRef?: ElementRef<HTMLElement>;

  readonly label = input.required<string>();
  readonly required = input<boolean>(false);
  readonly warning = input<string | null>(null);

  readonly fieldId = `form-field-${++nextFormFieldId}`;

  protected hasError = false;
  protected errorMessageValue: string | null = null;

  ngAfterContentInit(): void {
    const nativeElement = this.controlElementRef?.nativeElement;
    if (nativeElement && !nativeElement.hasAttribute('id')) {
      this.renderer.setAttribute(nativeElement, 'id', this.fieldId);
    }

    // Au tout premier passage, ngDoCheck s'exécute AVANT que la requête ContentChild ne soit
    // résolue (le contrôle projeté n'est donc pas encore visible) — on recalcule ici et on
    // resynchronise directement les attributs ARIA sur l'élément natif (Renderer2, indépendant
    // du rafraîchissement de gabarit Angular) pour qu'ils soient corrects dès le premier rendu,
    // sans provoquer de détection de changements imbriquée.
    this.recomputeState();
  }

  ngDoCheck(): void {
    this.recomputeState();
  }

  private recomputeState(): void {
    const control = this.ngControl?.control;
    const hasError = !!control && control.touched && control.invalid;
    const errorMessageValue = control && hasError ? getValidationErrorMessage(control.errors, this.label()) : null;

    // OnPush : ngDoCheck s'exécute à chaque passage de détection de changements (y compris
    // pour les vues OnPush), mais ne marque pas à lui seul cette vue pour rafraîchissement —
    // markForCheck() est nécessaire pour que le template (@if hasError) reflète l'état
    // recalculé issu d'un AbstractControl externe (touched/invalid non notifiés en Observable).
    if (hasError !== this.hasError || errorMessageValue !== this.errorMessageValue) {
      this.hasError = hasError;
      this.errorMessageValue = errorMessageValue;
      this.cdr.markForCheck();
    }

    this.syncAriaAttributes();
  }

  private syncAriaAttributes(): void {
    const nativeElement = this.controlElementRef?.nativeElement;
    if (!nativeElement) return;

    this.renderer.setAttribute(nativeElement, 'aria-invalid', String(this.hasError));

    // Bootstrap ne stylise que `.is-invalid`, jamais `[aria-invalid]` : sans cette classe, le
    // champ en erreur ne prend aucune bordure rouge alors que 11-ux-ui §3.1 en exige une de
    // 2 px. Le message sous le champ ne suffit pas à repérer le fautif dans un formulaire long.
    if (this.hasError) {
      this.renderer.addClass(nativeElement, 'is-invalid');
    } else {
      this.renderer.removeClass(nativeElement, 'is-invalid');
    }

    if (this.hasError) {
      this.renderer.setAttribute(nativeElement, 'aria-describedby', `${this.fieldId}-error`);
    } else if (this.warning()) {
      this.renderer.setAttribute(nativeElement, 'aria-describedby', `${this.fieldId}-warning`);
    } else {
      this.renderer.removeAttribute(nativeElement, 'aria-describedby');
    }
  }
}
