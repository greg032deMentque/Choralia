import { HttpTestingController } from '@angular/common/http/testing';

/**
 * Remplace `httpMock.verify()` dans les specs qui rendent un état d'erreur.
 *
 * Pourquoi : `IconComponent` charge ses SVG via un `HttpClient` construit sur `HttpBackend`,
 * donc les requêtes `GET /icons/{name}.svg` atterrissent dans `HttpTestingController` comme
 * n'importe quel appel métier. L'icône `wifi-slash` n'est rendue que par `DataStateComponent`
 * en état d'erreur : seuls les tests de chemin d'erreur la déclenchent, d'où la rareté du
 * symptôme.
 *
 * Pourquoi un helper plutôt qu'un filet global : la requête part de façon **asynchrone**
 * (`toObservable`/`toSignal`, planifiés par le scheduler d'`effect`). Tout nettoyage placé
 * hors du spec — `afterEach` global ou `onTestFinished` — s'exécute à un instant où la requête
 * n'est parfois **pas encore émise**, et elle atterrit alors juste avant le `verify()` du spec.
 * C'est une course, mesurée à 2 échecs sur 10 exécutions. Drainer ici, dans le même `afterEach`
 * et juste avant `verify()`, supprime la fenêtre.
 *
 * Ce que ce helper ne fait PAS : relâcher l'assertion. Toute requête **non liée aux icônes**
 * reste vérifiée par `verify()`, donc un appel HTTP métier inattendu échoue toujours le test —
 * c'est précisément ce que ces specs veulent prouver.
 */
export function verifyIgnoringIcons(httpMock: HttpTestingController): void {
  for (const request of httpMock.match(r => r.url.includes('/icons/'))) {
    request.flush('', { status: 200, statusText: 'OK' });
  }

  httpMock.verify();
}
