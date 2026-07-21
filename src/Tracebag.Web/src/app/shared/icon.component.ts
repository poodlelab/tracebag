import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

export type TracebagIcon =
  | 'activity'
  | 'archive'
  | 'arrow-left'
  | 'arrow-right'
  | 'box'
  | 'check'
  | 'chevron-down'
  | 'container'
  | 'download'
  | 'file'
  | 'gauge'
  | 'incident'
  | 'info'
  | 'layers'
  | 'list'
  | 'logout'
  | 'metrics'
  | 'play'
  | 'recording'
  | 'refresh'
  | 'search'
  | 'server'
  | 'settings'
  | 'stop'
  | 'terminal'
  | 'trash'
  | 'user'
  | 'warning';

@Component({
  selector: 'tb-icon',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
  template: `
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      @switch (name) {
        @case ('activity') { <path d="M3 12h4l2.2-6 4.2 12 2.3-6H21" /> }
        @case ('archive') { <path d="M4 7h16v13H4zM3 4h18v3H3zM9 11h6" /> }
        @case ('arrow-left') { <path d="m15 18-6-6 6-6M9 12h11" /> }
        @case ('arrow-right') { <path d="m9 18 6-6-6-6M4 12h11" /> }
        @case ('box') { <path d="m12 3 8 4.5v9L12 21l-8-4.5v-9zM4.5 7.7 12 12l7.5-4.3M12 12v9" /> }
        @case ('check') { <path d="m5 12 4 4L19 6" /> }
        @case ('chevron-down') { <path d="m7 10 5 5 5-5" /> }
        @case ('container') { <rect x="3" y="5" width="18" height="14" rx="2" /><path d="M7 9v6M11 9v6M15 9v6M19 9v6" /> }
        @case ('download') { <path d="M12 3v12m0 0 4-4m-4 4-4-4M4 19h16" /> }
        @case ('file') { <path d="M6 3h8l4 4v14H6zM14 3v5h5M9 13h6M9 17h6" /> }
        @case ('gauge') { <path d="M4.9 19a9 9 0 1 1 14.2 0M12 12l4-4M8 19h8" /> }
        @case ('incident') { <path d="M12 3 2.8 20h18.4zM12 9v4M12 17h.01" /> }
        @case ('info') { <circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7h.01" /> }
        @case ('layers') { <path d="m12 3 9 5-9 5-9-5zM3 12l9 5 9-5M3 16l9 5 9-5" /> }
        @case ('list') { <path d="M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01" /> }
        @case ('logout') { <path d="M10 4H5v16h5M14 8l4 4-4 4M8 12h10" /> }
        @case ('metrics') { <path d="M4 19V9M10 19V5M16 19v-7M22 19V8" /> }
        @case ('play') { <path d="m8 5 11 7-11 7z" /> }
        @case ('recording') { <circle cx="12" cy="12" r="8" /><circle cx="12" cy="12" r="3" /> }
        @case ('refresh') { <path d="M20 7v5h-5M4 17v-5h5M18.5 9A7 7 0 0 0 6 6.5L4 9m16 6-2 2.5A7 7 0 0 1 5.5 15" /> }
        @case ('search') { <circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /> }
        @case ('server') { <rect x="3" y="4" width="18" height="6" rx="2" /><rect x="3" y="14" width="18" height="6" rx="2" /><path d="M7 7h.01M7 17h.01M11 7h6M11 17h6" /> }
        @case ('settings') { <circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1-2.8 2.8-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6v.2h-4V21a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1L4.2 17l.1-.1a1.7 1.7 0 0 0 .3-1.9A1.7 1.7 0 0 0 3 14H2.8v-4H3a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9L4.2 7 7 4.2l.1.1a1.7 1.7 0 0 0 1.9.3A1.7 1.7 0 0 0 10 3V2.8h4V3a1.7 1.7 0 0 0 1 1.6 1.7 1.7 0 0 0 1.9-.3l.1-.1L19.8 7l-.1.1a1.7 1.7 0 0 0-.3 1.9 1.7 1.7 0 0 0 1.6 1h.2v4H21a1.7 1.7 0 0 0-1.6 1Z" /> }
        @case ('stop') { <rect x="6" y="6" width="12" height="12" rx="1" /> }
        @case ('terminal') { <rect x="3" y="4" width="18" height="16" rx="2" /><path d="m7 9 3 3-3 3M13 15h4" /> }
        @case ('trash') { <path d="M4 7h16M9 7V4h6v3M7 7l1 14h8l1-14M10 11v6M14 11v6" /> }
        @case ('user') { <circle cx="12" cy="8" r="4" /><path d="M4 21a8 8 0 0 1 16 0" /> }
        @case ('warning') { <path d="M12 3 2.8 20h18.4zM12 9v4M12 17h.01" /> }
      }
    </svg>
  `,
  styles: [`
    :host { display: inline-flex; flex: 0 0 auto; height: 1.15em; width: 1.15em; }
    svg { display: block; height: 100%; width: 100%; }
  `]
})
export class IconComponent {
  @Input({ required: true }) name!: TracebagIcon;
}
