import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { FormFieldComponent } from './form-field.component';

@Component({
  standalone: true,
  imports: [FormFieldComponent, ReactiveFormsModule],
  template: `
    <app-form-field [label]="'Titre'" [required]="true" [warning]="warning()">
      <input class="form-control" [formControl]="control" />
    </app-form-field>
  `
})
class HostComponent {
  private readonly fb = inject(FormBuilder);
  readonly control = this.fb.nonNullable.control('', Validators.required);
  // Signal (pas un champ mutable nu) : ce projet est en détection de changements zoneless —
  // une mutation directe d'un champ, sans passer par un signal, n'est pas retraversée de
  // façon fiable par un second fixture.detectChanges() (NG0100 en environnement de test).
  readonly warning = signal<string | null>(null);
}

describe('FormFieldComponent', () => {
  function createHost() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const input: HTMLInputElement = fixture.nativeElement.querySelector('input');
    return { fixture, input, host: fixture.componentInstance };
  }

  it('affiche le message d’erreur sous le champ quand il est obligatoire, vide et touché', () => {
    const { fixture, host } = createHost();

    host.control.markAsTouched();
    host.control.updateValueAndValidity();
    fixture.detectChanges();

    const errorEl: HTMLElement | null = fixture.nativeElement.querySelector('.form-field__error');
    expect(errorEl?.textContent?.trim()).toBe('Titre est obligatoire.');
  });

  it('ne montre aucune erreur tant que le champ n’a pas été touché', () => {
    const { fixture } = createHost();

    const errorEl: HTMLElement | null = fixture.nativeElement.querySelector('.form-field__error');
    expect(errorEl).toBeNull();
  });

  it('positionne aria-invalid et aria-describedby correctement sur le champ projeté', () => {
    const { fixture, input, host } = createHost();

    expect(input.getAttribute('aria-invalid')).toBe('false');
    expect(input.hasAttribute('aria-describedby')).toBe(false);

    host.control.markAsTouched();
    host.control.updateValueAndValidity();
    fixture.detectChanges();

    expect(input.getAttribute('aria-invalid')).toBe('true');
    const describedBy = input.getAttribute('aria-describedby');
    expect(describedBy).toBeTruthy();

    const errorEl = describedBy ? document.getElementById(describedBy) : null;
    expect(errorEl?.classList.contains('form-field__error')).toBe(true);
  });

  it('affiche le message d’avertissement sans marquer le champ en erreur', () => {
    const { fixture, input, host } = createHost();

    host.warning.set('Ce chant est déjà présent dans une autre liste.');
    fixture.detectChanges();

    const warningEl: HTMLElement | null = fixture.nativeElement.querySelector('.form-field__warning');
    expect(warningEl?.textContent?.trim()).toBe('Ce chant est déjà présent dans une autre liste.');
    expect(fixture.nativeElement.querySelector('.form-field__error')).toBeNull();
    expect(input.getAttribute('aria-invalid')).toBe('false');

    const describedBy = input.getAttribute('aria-describedby');
    const describedEl = describedBy ? document.getElementById(describedBy) : null;
    expect(describedEl?.classList.contains('form-field__warning')).toBe(true);
  });
});
