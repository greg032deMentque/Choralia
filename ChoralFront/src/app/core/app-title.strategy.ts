import { Injectable, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

const APP_NAME = 'Choralia';

// Sans stratégie de titre, Angular laisse le <title> figé sur celui d'index.html : les ~26
// `data.title` déclarés dans app.routes.ts n'étaient jamais lus (code mort), l'historique et
// les onglets du navigateur étaient inexploitables, et WCAG 2.4.2 (« Titre de page ») n'était
// pas tenu. Le nom du produit ferme le titre plutôt qu'il ne l'ouvre : c'est la partie
// distinctive qui doit rester visible quand l'onglet est étroit.
@Injectable({ providedIn: 'root' })
export class AppTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    const routeTitle = this.buildTitle(snapshot);
    this.title.setTitle(routeTitle ? `${routeTitle} — ${APP_NAME}` : APP_NAME);
  }
}
