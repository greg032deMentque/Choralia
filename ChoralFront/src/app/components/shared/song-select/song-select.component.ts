import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, output, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SongService } from '@app/services/songs/song.service';
import { AuthStore } from '@core/auth.store';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { ISelectOption } from '@models/common-models/select-option.model';

/**
 * Champ « Chant » d'un formulaire de dépôt (partition, enregistrement) : le chant porteur y est
 * une donnée obligatoire du formulaire, pas un filtre de navigation.
 *
 * Distinct de <see cref="SongPickerComponent"/> et il doit le rester : celui-là est un filtre
 * (option neutre sélectionnable, impasse du répertoire vide à expliquer, aucune validation),
 * celui-ci un contrôle réactif validé (option neutre `disabled`, message d'erreur). Les fondre
 * donnerait un composant à deux personnalités, pire que la duplication qu'on retire. Ce qu'ils
 * partagent réellement — le chargement du répertoire et son plafond — vit dans
 * `SongService.getChoirOptions`.
 *
 * Le contrôle est reçu du parent plutôt que réimplémenté en ControlValueAccessor : le
 * formulaire reste seul propriétaire de son état, et `FormFieldComponent` retrouve le
 * `NgControl` projeté pour câbler label, aria et message d'erreur — y compris la fraîcheur de
 * `touched`/`invalid`, qui ne sont pas des signals (voir son `ngDoCheck`).
 */
@Component({
  selector: 'app-song-select',
  standalone: true,
  imports: [ReactiveFormsModule, FormFieldComponent],
  templateUrl: './song-select.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongSelectComponent {
  private readonly songService = inject(SongService);
  private readonly authStore = inject(AuthStore);
  private readonly destroyRef = inject(DestroyRef);

  readonly control = input.required<FormControl<string>>();

  /** L'appelant reste maître de son affichage d'erreur (état inline, jamais un toast ici). */
  readonly loadFailed = output();

  protected readonly options = signal<ISelectOption<string>[]>([]);

  constructor() {
    const choirId = this.authStore.activeSpaceId();
    if (!choirId) return;

    this.songService
      .getChoirOptions(choirId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: options => this.options.set(options),
        error: () => this.loadFailed.emit()
      });
  }
}
