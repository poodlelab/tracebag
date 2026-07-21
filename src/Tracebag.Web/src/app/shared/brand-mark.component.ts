import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'tb-brand-mark',
  standalone: true,
  template: '<img src="brand/tracebag-mark.svg" alt="" width="64" height="64">',
  host: {
    class: 'brand-mark',
    'aria-hidden': 'true'
  },
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class BrandMarkComponent {}
