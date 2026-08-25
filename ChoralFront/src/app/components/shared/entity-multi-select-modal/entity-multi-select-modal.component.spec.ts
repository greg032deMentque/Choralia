import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { EntityMultiSelectModalComponent, EntitySearchFn } from './entity-multi-select-modal.component';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';
import { verifyIgnoringIcons } from '@app/testing/verify-ignoring-icons';
import { ISelectOption } from '@models/common-models/select-option.model';

// Bug corrigé : `searchFn` (input.required) et `initialSelection` étaient lus directement dans
// le constructeur, avant qu'Angular n'ait appliqué le binding des inputs — NG0950 ("Input is
// required but no value was set") systématique à l'ouverture de la modale, quel que soit le
// binding côté template (voir user-list.component.html:208). Ce test échoue avant le correctif
// (effect() + untracked() dans le constructeur) et passe après.
describe('EntityMultiSelectModalComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    stubIconHttpRequests();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    verifyIgnoringIcons(httpMock);
  });

  it("ne lève pas NG0950 au montage et déclenche un chargement initial via searchFn", () => {
    const searchFn: EntitySearchFn = vi.fn(() => of({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 }));

    const fixture = TestBed.createComponent(EntityMultiSelectModalComponent);
    fixture.componentRef.setInput('title', 'Choisir des chorales');
    fixture.componentRef.setInput('searchFn', searchFn);

    expect(() => fixture.detectChanges()).not.toThrow();
    expect(searchFn).toHaveBeenCalledTimes(1);
  });

  it('reprend la présélection transmise via initialSelection et la restitue au clic sur Valider', () => {
    const preselected: ISelectOption<string>[] = [{ Value: 'choir-1', Label: 'Chorale Test' }];
    const searchFn: EntitySearchFn = vi.fn(() => of({ Items: [], TotalCount: 0, CurrentPage: 1, PageSize: 20 }));

    const fixture = TestBed.createComponent(EntityMultiSelectModalComponent);
    fixture.componentRef.setInput('title', 'Choisir des chorales');
    fixture.componentRef.setInput('searchFn', searchFn);
    fixture.componentRef.setInput('initialSelection', preselected);

    const confirmedSpy = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmedSpy);

    fixture.detectChanges();

    const count: HTMLElement = fixture.nativeElement.querySelector('.entity-multi-select-modal__count');
    expect(count.textContent).toContain('1 élément sélectionné');

    const confirmButton: HTMLButtonElement = fixture.nativeElement.querySelector('button.btn-primary');
    confirmButton.click();

    expect(confirmedSpy).toHaveBeenCalledWith(preselected);
    expect(searchFn).toHaveBeenCalledTimes(1);
  });
});
