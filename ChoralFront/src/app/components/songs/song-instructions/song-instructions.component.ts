import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { PaginationComponent } from '@app/components/shared/pagination/pagination.component';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, tap, throwError } from 'rxjs';
import { InstructionService } from '@app/services/instructions/instruction.service';
import { ConfirmService } from '@app/services/confirm.service';
import { ToastService } from '@app/services/toast.service';
import { DataStateComponent } from '@app/components/shared/data-state/data-state.component';
import { FormFieldComponent } from '@app/components/shared/form-field/form-field.component';
import { IconComponent } from '@app/components/shared/icon/icon.component';
import { SubmitOnceDirective } from '@app/components/shared/submit-once/submit-once.directive';
import { IconNameEnum } from '@app/enums/icon-name.enum';
import { InstructionStatusEnum, getStatusInstructionLabel } from '@app/enums/status-instruction.enum';
import { VoicePartEnum, getVoicePartLabel } from '@app/enums/voice-part.enum';
import { IInstruction } from '@models/instructions-models/instruction.model';

const PAGE_SIZE = 10;

// Consignes attachées à UN chant — seule portée existante depuis la migration
// InstructionsSongScopeOnly. Le champ « Voix ciblée » est optionnel : vide = consigne pour tout
// le chœur (réservée au responsable côté serveur), une voix = consigne de pupitre, seul cas
// ouvert au chef de pupitre et uniquement sur SA voix.
//
// Le front ne connaît PAS la voix dirigée par un chef de pupitre : AuthStore porte le rôle
// (SectionLeader) mais pas le pupitre, qui vit dans Section.SectionLeaderId côté back. Aucun
// pré-remplissage n'est donc possible — c'est le 403 du serveur qui arbitre, jamais une règle
// réécrite ici (voir CLAUDE.md : le front décide quoi afficher, le serveur quoi autoriser).
@Component({
  selector: 'app-song-instructions',
  standalone: true,
  imports: [PaginationComponent, 
    DatePipe,
    ReactiveFormsModule,
    DataStateComponent,
    FormFieldComponent,
    IconComponent,
    SubmitOnceDirective
  ],
  templateUrl: './song-instructions.component.html',
  styleUrl: './song-instructions.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SongInstructionsComponent {
  private readonly instructionService = inject(InstructionService);
  private readonly confirmService = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly songId = input.required<string>();
  readonly canManage = input<boolean>(false);

  protected readonly IconNameEnum = IconNameEnum;
  protected readonly InstructionStatusEnum = InstructionStatusEnum;
  protected readonly getStatusInstructionLabel = getStatusInstructionLabel;
  protected readonly getVoicePartLabel = getVoicePartLabel;
  protected readonly allVoicePart: VoicePartEnum[] = [
    VoicePartEnum.Soprano,
    VoicePartEnum.Alto,
    VoicePartEnum.Tenor,
    VoicePartEnum.Bass
  ];

  readonly items = signal<IInstruction[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly page = signal(1);
  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));

  readonly showCreateForm = signal(false);
  readonly editingId = signal<string | null>(null);

  readonly createForm = this.fb.nonNullable.group({
    voicePart: this.fb.nonNullable.control<string>(''),
    title: this.fb.nonNullable.control(''),
    content: this.fb.nonNullable.control('', [Validators.required])
  });

  readonly editForm = this.fb.nonNullable.group({
    title: this.fb.nonNullable.control(''),
    content: this.fb.nonNullable.control('', [Validators.required])
  });

  constructor() {
    // `songId` est un signal input : il n'est peuplé qu'après la construction. Un effect
    // recharge aussi quand on navigue d'un chant à un autre sans détruire le composant.
    //
    // `untracked` est indispensable : `load()` lit `page()`, donc l'appeler directement dans
    // l'effect faisait traquer `page`. Changer de page invalidait l'effect, qui se rejouait,
    // remettait `page` à 1 et rechargeait — pagination morte et deux requêtes par clic. Ici
    // l'effect n'a qu'une dépendance, `songId`, et c'est bien la seule voulue.
    effect(() => {
      this.songId();
      untracked(() => {
        this.page.set(1);
        this.load();
      });
    });
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    this.load();
  }

  toggleCreateForm(): void {
    this.showCreateForm.update(open => !open);
    this.editingId.set(null);
    if (!this.showCreateForm()) {
      this.createForm.reset({ voicePart: '', title: '', content: '' });
    }
  }

  // Panneau refermé au succès : SubmitOnceDirective reste désactivée après un succès, le
  // laisser ouvert interdirait une seconde création.
  submitCreate = (): Observable<IInstruction> => {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    const { voicePart, title, content } = this.createForm.getRawValue();

    return this.instructionService
      .create({
        SongId: this.songId(),
        VoicePart: voicePart === '' ? undefined : (Number(voicePart) as VoicePartEnum),
        Title: title.trim() || undefined,
        Content: content
      })
      .pipe(
        tap(() => {
          this.toast.success('Consigne créée en brouillon.');
          this.createForm.reset({ voicePart: '', title: '', content: '' });
          this.showCreateForm.set(false);
          this.load();
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };

  startEdit(instruction: IInstruction): void {
    if (!instruction.Id) return;
    this.showCreateForm.set(false);
    this.editingId.set(instruction.Id);
    this.editForm.reset({ title: instruction.Title ?? '', content: instruction.Content });
  }

  cancelEdit(): void {
    this.editingId.set(null);
  }

  // Update ne transporte QUE Title et Content : la voix ciblée est figée à la création côté
  // back (UpdateInstructionViewModel), la modifier exige de recréer la consigne.
  submitEdit = (): Observable<IInstruction> => {
    const id = this.editingId();
    if (!id || this.editForm.invalid) {
      this.editForm.markAllAsTouched();
      return throwError(() => new Error('validation'));
    }

    const { title, content } = this.editForm.getRawValue();

    return this.instructionService
      .update({ Id: id, Title: title.trim() || undefined, Content: content })
      .pipe(
        tap(() => {
          this.editingId.set(null);
          this.load();
        }),
        takeUntilDestroyed(this.destroyRef)
      );
  };

  publish(instruction: IInstruction): void {
    if (!instruction.Id) return;
    this.instructionService
      .publish(instruction.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Consigne publiée.');
          this.load();
        },
        error: () => undefined
      });
  }

  archive(instruction: IInstruction): void {
    if (!instruction.Id) return;
    this.instructionService
      .archive(instruction.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Consigne archivée.');
          this.load();
        },
        error: () => undefined
      });
  }

  // Suppression définitive, sans inverse côté API : modale de confirmation (Spec §6.4, 10-D42).
  async deleteInstruction(instruction: IInstruction): Promise<void> {
    if (!instruction.Id) return;

    const confirmed = await this.confirmService.confirm({
      title: 'Supprimer cette consigne',
      message: instruction.Title
        ? `« ${instruction.Title} » sera supprimée définitivement.`
        : 'Cette consigne sera supprimée définitivement.',
      impacts: ['Pour la retirer sans la perdre, archivez-la à la place.'],
      confirmationLabel: 'Supprimer',
      danger: true
    });
    if (!confirmed) return;

    this.instructionService
      .delete(instruction.Id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.toast.success('Consigne supprimée.');
          this.load();
        },
        error: () => undefined
      });
  }

  private load(): void {
    const songId = this.songId();
    if (!songId) return;

    this.loading.set(true);
    this.error.set(null);

    this.instructionService
      .getPaged({ SongId: songId }, { Page: this.page(), PageSize: PAGE_SIZE })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: result => {
          this.items.set(result.Items);
          this.totalCount.set(result.TotalCount);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set('Impossible de charger les consignes de ce chant.');
        }
      });
  }
}
