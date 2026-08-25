import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { Observable, catchError, of, shareReplay, switchMap } from 'rxjs';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { IconNameEnum } from '@app/enums/icon-name.enum';

// Charge en inline les SVG Phosphor depuis public/icons/{name}.svg (Assets/icons/ source,
// copiés au build). bypassSecurityTrustHtml est justifié ici : les SVG proviennent
// exclusivement de cet asset statique contrôlé par le dépôt, jamais d'une entrée
// utilisateur ni d'une réponse API — pas de vecteur XSS (OWASP A03).
const svgCache = new Map<string, Observable<SafeHtml>>();

/**
 * Vide le cache module-level des SVG chargés. En production ce cache doit vivre pour toute la
 * durée de l'application (évite de recharger le même SVG à chaque instance d'IconComponent) —
 * cette fonction n'a de sens qu'en test.
 *
 * `svgCache` est un singleton au niveau module, et le builder de tests Angular exécute les
 * fichiers de specs avec `isolate: false` (config Vitest par défaut du builder — voir
 * angular.json), donc ce module n'est chargé qu'une seule fois pour tous les fichiers d'un même
 * worker : un Observable mis en cache par un test peut fuiter vers un test suivant.
 *
 * NE PEUT PAS être appelée depuis `testing/global-test-setup.ts` (setupFiles) : constaté
 * empiriquement, ce builder exécute les `setupFiles` HORS du graphe de modules compilé de
 * l'application — un import de ce fichier depuis un setupFile (relatif ou via l'alias @app/)
 * résout vers une INSTANCE DE MODULE SÉPARÉE, avec son propre `svgCache` distinct de celui
 * réellement utilisé par les composants rendus dans les tests. Purger ce second cache ne
 * purgerait rien d'observable. Cette fonction reste disponible pour tout code qui, lui, fait
 * partie du graphe compilé (un spec, ou du code applicatif) — voir le risque néanmoins neutralisé
 * autrement dans `testing/global-test-setup.ts` (flush systématique des requêtes /icons/ dans
 * HttpTestingController avant `verify()`, qui empêche qu'un Observable reste indéfiniment en
 * attente dans le cache).
 */
export function resetIconSvgCache(): void {
  svgCache.clear();
}

@Component({
  selector: 'app-icon',
  standalone: true,
  template: `
    <span
      class="app-icon"
      [class]="'app-icon--' + size()"
      [innerHTML]="svgContent()"
      [attr.aria-hidden]="ariaLabel() ? null : 'true'"
      [attr.aria-label]="ariaLabel()"
      [attr.role]="ariaLabel() ? 'img' : null"
    ></span>
  `,
  styleUrl: './icon.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class IconComponent {
  // HttpClient construit à partir de HttpBackend : contourne volontairement
  // tokenInterceptor/apiErrorInterceptor. Un SVG statique manquant (404) ne doit
  // déclencher ni Bearer token ni toast d'erreur global — seul le fallback silencieux
  // ci-dessous s'applique.
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly sanitizer = inject(DomSanitizer);

  readonly name = input.required<IconNameEnum>();
  readonly size = input<'dense' | 'inline' | 'nav' | 'empty-state'>('inline');
  readonly ariaLabel = input<string | null>(null);

  private readonly name$ = toObservable(this.name);

  readonly svgContent = toSignal(
    this.name$.pipe(switchMap(name => this.loadSvg(name))),
    { initialValue: null }
  );

  private loadSvg(name: IconNameEnum): Observable<SafeHtml> {
    const cached = svgCache.get(name);
    if (cached) return cached;

    const svg$ = this.http.get(`/icons/${name}.svg`, { responseType: 'text' }).pipe(
      catchError(() => of('')),
      switchMap(raw => of(this.sanitizer.bypassSecurityTrustHtml(raw))),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    svgCache.set(name, svg$);
    return svg$;
  }
}
