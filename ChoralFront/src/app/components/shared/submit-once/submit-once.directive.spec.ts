import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Observable, Subject } from 'rxjs';
import { SubmitOnceDirective } from './submit-once.directive';

@Component({
  standalone: true,
  imports: [SubmitOnceDirective],
  template: `<button type="button" [appSubmitOnce]="action">Enregistrer</button>`
})
class HostComponent {
  readonly trigger$ = new Subject<void>();
  callCount = 0;

  readonly action = (): Observable<void> => {
    this.callCount++;
    return this.trigger$.asObservable();
  };
}

describe('SubmitOnceDirective', () => {
  function createHost() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button');
    return { fixture, button, host: fixture.componentInstance };
  }

  it('un double clic rapide ne déclenche qu’un seul appel de l’action', () => {
    const { fixture, button, host } = createHost();

    button.click();
    button.click();
    fixture.detectChanges();

    expect(host.callCount).toBe(1);
  });

  it('réactive le bouton en cas d’erreur (sinon l’utilisateur reste bloqué définitivement)', () => {
    const { fixture, button, host } = createHost();

    button.click();
    fixture.detectChanges();
    expect(button.disabled).toBe(true);

    host.trigger$.error(new Error('échec réseau'));
    fixture.detectChanges();

    expect(button.disabled).toBe(false);

    // Un nouveau clic doit redéclencher l'action (le verrou a bien été levé).
    button.click();
    fixture.detectChanges();
    expect(host.callCount).toBe(2);
  });

  it('reste désactivé après un succès', () => {
    const { fixture, button, host } = createHost();

    button.click();
    fixture.detectChanges();

    host.trigger$.next();
    host.trigger$.complete();
    fixture.detectChanges();

    expect(button.disabled).toBe(true);
  });

  it('positionne aria-busy pendant l’action puis le retire à la fin', () => {
    const { fixture, button, host } = createHost();

    expect(button.getAttribute('aria-busy')).toBe('false');

    button.click();
    fixture.detectChanges();
    expect(button.getAttribute('aria-busy')).toBe('true');

    host.trigger$.next();
    host.trigger$.complete();
    fixture.detectChanges();

    expect(button.getAttribute('aria-busy')).toBe('false');
  });

  it('ne fait jamais disparaître le libellé du bouton pendant le chargement', () => {
    const { fixture, button } = createHost();

    button.click();
    fixture.detectChanges();

    expect(button.textContent).toContain('Enregistrer');
  });
});
