import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { IconComponent } from './icon.component';

@Component({
  selector: 'tb-confirm-dialog',
  standalone: true,
  imports: [IconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="dialog-backdrop" (click)="cancelled.emit()">
      <section class="dialog-card" role="alertdialog" aria-modal="true" [attr.aria-labelledby]="dialogId + '-title'" [attr.aria-describedby]="dialogId + '-description'" (click)="$event.stopPropagation()">
        <span class="dialog-icon"><tb-icon name="warning" /></span>
        <div class="dialog-copy">
          <h2 [id]="dialogId + '-title'">{{ title }}</h2>
          <p [id]="dialogId + '-description'">{{ message }}</p>
        </div>
        <div class="dialog-actions">
          <button type="button" (click)="cancelled.emit()">Cancel</button>
          <button type="button" class="danger" (click)="confirmed.emit()">{{ confirmLabel }}</button>
        </div>
      </section>
    </div>
  `,
  styles: [`
    .dialog-backdrop { align-items: center; background: rgb(2 6 10 / 72%); display: flex; inset: 0; justify-content: center; padding: 20px; position: fixed; z-index: 100; backdrop-filter: blur(5px); }
    .dialog-card { background: var(--surface-raised); border: 1px solid var(--line-strong); border-radius: var(--radius-lg); box-shadow: var(--shadow); display: grid; gap: 14px; grid-template-columns: auto minmax(0, 1fr); max-width: 440px; padding: 20px; width: 100%; }
    .dialog-icon { align-items: center; background: var(--danger-soft); border-radius: 9px; color: var(--danger); display: flex; font-size: 1.25rem; height: 40px; justify-content: center; width: 40px; }
    .dialog-copy { display: grid; gap: 7px; }
    h2 { font-size: 1rem; margin: 0; }
    p { color: var(--muted-strong); font-size: .8rem; line-height: 1.55; margin: 0; }
    .dialog-actions { display: flex; gap: 8px; grid-column: 1 / -1; justify-content: flex-end; margin-top: 4px; }
  `]
})
export class ConfirmDialogComponent {
  private static nextId = 0;
  readonly dialogId = `tb-confirm-dialog-${ConfirmDialogComponent.nextId++}`;

  @Input({ required: true }) title = '';
  @Input({ required: true }) message = '';
  @Input() confirmLabel = 'Confirm';
  @Output() readonly confirmed = new EventEmitter<void>();
  @Output() readonly cancelled = new EventEmitter<void>();
}
