import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { DataTableComponent, IDataTableColumn } from './data-table.component';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { stubIconHttpRequests } from '@app/testing/icon-http-stub';

interface IRow {
  Name: string;
  Age: number;
}

const columns: IDataTableColumn<IRow>[] = [
  { key: 'Name', label: 'Nom', sortable: true },
  { key: 'Age', label: 'Âge', sortable: true }
];

@Component({
  standalone: true,
  imports: [DataTableComponent],
  template: `
    <app-data-table
      [columns]="columns"
      [items]="items()"
      [totalCount]="totalCount()"
      [page]="page()"
      [pageSize]="pageSize()"
      [sortActive]="sortActive()"
      [sortDirection]="sortDirection()"
      [loading]="loading()"
      [error]="error()"
      (sortChange)="sortEvents.push($event)"
      (pageChange)="pageEvents.push($event)"
      (filterChange)="filterEvents.push($event)"
    />
  `
})
class HostComponent {
  readonly columns = columns;
  readonly items = signal<IRow[]>([{ Name: 'Alice', Age: 30 }]);
  readonly totalCount = signal(1);
  readonly page = signal(1);
  readonly pageSize = signal(10);
  readonly sortActive = signal<string | undefined>(undefined);
  readonly sortDirection = signal<'asc' | 'desc' | undefined>(undefined);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly sortEvents: { active: string; direction: 'asc' | 'desc' }[] = [];
  readonly pageEvents: number[] = [];
  readonly filterEvents: string[] = [];
}

describe('DataTableComponent', () => {
  function createHost() {
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance };
  }

  it('trie par en-tête à la souris ET au clavier', () => {
    const { fixture, host } = createHost();

    const headers: HTMLElement[] = fixture.nativeElement.querySelectorAll("th[role='button']");
    headers[0].dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(host.sortEvents).toEqual([{ active: 'Name', direction: 'asc' }]);

    // (keydown.enter) est un alias Angular sur l'événement natif 'keydown' filtré par touche —
    // on déclenche donc un vrai 'keydown' avec cle: 'Enter'.
    headers[1].dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    fixture.detectChanges();

    expect(host.sortEvents).toEqual([
      { active: 'Name', direction: 'asc' },
      { active: 'Age', direction: 'asc' }
    ]);
  });

  it('recule automatiquement d’une page quand la page courante devient vide après une suppression', async () => {
    const { fixture, host } = createHost();

    host.page.set(2);
    host.items.set([]);
    host.totalCount.set(10);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.pageEvents).toContain(1);
  });

  it('ne recule pas de page quand la liste est vide à cause d’un filtre actif (page 1)', async () => {
    const { fixture, host } = createHost();

    const table = fixture.debugElement.query(By.directive(DataTableComponent)).componentInstance as DataTableComponent<IRow>;
    table.onFilterInput('notFound');
    host.items.set([]);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(host.pageEvents).toEqual([]);
  });

  it('affiche un message distinct pour "aucun résultat pour ce filtre" vs "aucune donnée"', () => {
    const { fixture } = createHost();

    // Aucune donnée du tout (pas de filtre actif) : message par défaut.
    fixture.componentInstance.items.set([]);
    fixture.detectChanges();
    let emptyEl: HTMLElement = fixture.nativeElement.querySelector('.data-state__message');
    expect(emptyEl.textContent?.trim()).toBe('Aucune donnée pour le moment.');

    // Filtre actif sans résultat : message différent.
    const table = fixture.debugElement.query(By.directive(DataTableComponent)).componentInstance as DataTableComponent<IRow>;
    table.onFilterInput('notFound');
    fixture.detectChanges();
    emptyEl = fixture.nativeElement.querySelector('.data-state__message');
    expect(emptyEl.textContent?.trim()).toBe('Aucun résultat pour ce filtre.');
  });

  it('désactive réellement (attribut [disabled]) les boutons de pagination en première et dernière page', () => {
    const { fixture, host } = createHost();

    host.totalCount.set(25);
    host.pageSize.set(10);
    host.page.set(1);
    fixture.detectChanges();

    const nativeElement = fixture.nativeElement as HTMLElement;
    const [prevBtn1, nextBtn1] = nativeElement.querySelectorAll<HTMLButtonElement>('.pagination button');
    expect(prevBtn1.disabled).toBe(true);
    expect(nextBtn1.disabled).toBe(false);

    host.page.set(3);
    fixture.detectChanges();

    const [prevBtn2, nextBtn2] = nativeElement.querySelectorAll<HTMLButtonElement>('.pagination button');
    expect(prevBtn2.disabled).toBe(false);
    expect(nextBtn2.disabled).toBe(true);
  });

  it('anti-rebond : plusieurs frappes rapides ne déclenchent qu’un seul filterChange', () => {
    vi.useFakeTimers();
    try {
      const { fixture, host } = createHost();
      const table = fixture.debugElement.query(By.directive(DataTableComponent)).componentInstance as DataTableComponent<IRow>;

      table.onFilterInput('a');
      table.onFilterInput('al');
      table.onFilterInput('ali');
      table.onFilterInput('alice');

      vi.advanceTimersByTime(300);

      expect(host.filterEvents).toEqual(['alice']);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('DataStateComponent', () => {
  it('affiche Réessayer uniquement en erreur et émet une seule demande de relance', () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    stubIconHttpRequests();
    const fixture = TestBed.createComponent(DataStateComponent);
    fixture.componentRef.setInput('retryLabel', 'Réessayer');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('button')).toBeNull();

    let retryCount = 0;
    fixture.componentInstance.retry.subscribe(() => retryCount++);
    fixture.componentRef.setInput('error', 'Chargement impossible.');
    fixture.detectChanges();
    const retryButton = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button');
    retryButton?.click();

    expect(retryButton?.textContent?.trim()).toBe('Réessayer');
    expect(retryCount).toBe(1);
  });
});
