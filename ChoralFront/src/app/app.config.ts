import { ApplicationConfig, LOCALE_ID, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeFr from '@angular/common/locales/fr';
import { provideRouter, withComponentInputBinding, TitleStrategy } from '@angular/router';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideToastr } from 'ngx-toastr';
import { routes } from './app.routes';
import { AppTitleStrategy } from '@core/app-title.strategy';
import { tokenInterceptor, apiErrorInterceptor } from '@app/interceptor';
import { AuthService } from '@app/services/auth/auth.service';

// Locale française enregistrée une seule fois au démarrage : sans registerLocaleData +
// LOCALE_ID, Angular retombe sur en-US par défaut (dates au format M/d/yy) alors que
// l'application n'a jamais eu d'autre locale que le français.
registerLocaleData(localeFr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    { provide: LOCALE_ID, useValue: 'fr-FR' },
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withInterceptors([tokenInterceptor, apiErrorInterceptor])),
    // Pas de provideAnimations() : évite la dépendance @angular/animations non
    // validée dans le plan. ngx-toastr reste fonctionnel avec son CSS de transition
    // natif (toastr.css) — seules les animations Angular avancées sont désactivées.
    provideToastr({
      maxOpened: 3,
      preventDuplicates: true,
      positionClass: 'toast-bottom-right'
    }),
    { provide: TitleStrategy, useClass: AppTitleStrategy },
    provideAppInitializer(() => inject(AuthService).initializeSession())
  ]
};
