import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmDialogComponent } from './confirm-dialog.component';

describe('ConfirmDialogComponent', () => {
  it('exposes an accessible destructive confirmation and keeps cancel separate', () => {
    const fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.componentRef.setInput('title', 'Delete incident?');
    fixture.componentRef.setInput('message', 'This cannot be undone.');
    fixture.componentRef.setInput('confirmLabel', 'Delete incident');
    const confirmed = vi.fn();
    const cancelled = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentInstance.cancelled.subscribe(cancelled);
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('[role="alertdialog"]') as HTMLElement;
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    const buttons = fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>;
    buttons[0].click();
    expect(cancelled).toHaveBeenCalledOnce();
    expect(confirmed).not.toHaveBeenCalled();

    buttons[1].click();
    expect(confirmed).toHaveBeenCalledOnce();
  });
});
