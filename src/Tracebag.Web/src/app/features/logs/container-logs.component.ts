import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { LogSearchEntryDto, LogSearchFilters } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';

@Component({
  selector: 'tb-container-logs',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">searchable logs</span>
        <span class="workspace-target">{{ selectedContainerName() }}</span>
        <span class="status-pill" [class.ok]="liveState() === 'live'">{{ liveState() }}</span>
      </div>

      <form class="search-grid" (ngSubmit)="search(true)">
        <label class="wide">full-text search
          <input name="text" [(ngModel)]="filters.text" placeholder="message, exception, or field value">
        </label>
        <label>level
          <select name="level" [(ngModel)]="filters.level">
            <option value="">all levels</option>
            <option value="trace">trace</option>
            <option value="debug">debug</option>
            <option value="information">information</option>
            <option value="warning">warning</option>
            <option value="error">error</option>
            <option value="critical">critical</option>
          </select>
        </label>
        <label>stream
          <select name="stream" [(ngModel)]="filters.stream">
            <option value="">stdout + stderr</option>
            <option value="stdout">stdout</option>
            <option value="stderr">stderr</option>
          </select>
        </label>
        <label>from
          <input name="from" type="datetime-local" [(ngModel)]="fromLocal">
        </label>
        <label>to
          <input name="to" type="datetime-local" [(ngModel)]="toLocal">
        </label>
        <label class="check"><input name="exceptions" type="checkbox" [(ngModel)]="filters.exceptionOnly"> exceptions only</label>
        <button type="submit">Search</button>
      </form>

      <div class="command-row">
        <button type="button" [class.live]="liveState() === 'live'" (click)="toggleLive()">
          {{ liveSource ? 'Stop live' : 'Follow live' }}
        </button>
        <button type="button" (click)="togglePause()">{{ paused() ? 'Resume' : 'Pause' }}</button>
        <button type="button" (click)="downloadVisible()">Download visible</button>
        <button type="button" (click)="clear()">Clear</button>
        <span>{{ entries().length }} loaded</span>
        @if (pausedCount()) { <span class="notice">{{ pausedCount() }} live entries waiting for refresh</span> }
      </div>

      <input class="filter" placeholder="search within loaded results" [ngModel]="viewFilter()" (ngModelChange)="viewFilter.set($event)">

      @if (loading()) {
        <div class="empty-state rich-empty"><span class="spinner small"></span><strong>Searching indexed logs</strong></div>
      } @else if (!visibleEntries().length) {
        <div class="empty-state rich-empty"><strong>No matching logs</strong><span>Adjust the search or time range and try again.</span></div>
      } @else {
        <div class="persisted-log-view">
          @for (entry of visibleEntries(); track entry.id) {
            <article class="log-row" [class.stderr]="entry.stream === 'stderr'">
              <time>{{ entry.timestamp | date: 'medium' }}</time>
              <span class="level" [attr.data-level]="entry.level || 'none'">{{ entry.level || entry.stream }}</span>
              <pre>{{ entry.message }}</pre>
              @if (entry.exceptionType || entry.traceId) {
                <small>{{ entry.exceptionType }} @if (entry.traceId) { · trace {{ entry.traceId }} }</small>
              }
            </article>
          }
        </div>
      }

      @if (hasMore()) {
        <button type="button" class="load-more" (click)="search(false)">Load older results</button>
      }
    </section>
  `,
  styles: [`
    .search-grid { display: grid; grid-template-columns: minmax(220px, 2fr) repeat(3, minmax(130px, 1fr)); gap: .75rem; margin: 0; padding: 1rem; align-items: end; }
    .search-grid > *, .search-grid input, .search-grid select { min-width: 0; width: 100%; }
    .search-grid label { display: grid; gap: .35rem; font-size: .78rem; color: var(--muted); }
    .search-grid .check { display: flex; align-items: center; gap: .45rem; }
    .persisted-log-view { max-height: 62vh; overflow: auto; border: 1px solid var(--line); background: #07100f; }
    .log-row { display: grid; grid-template-columns: 165px 90px 1fr; gap: .7rem; padding: .5rem .7rem; border-bottom: 1px solid color-mix(in srgb, var(--line) 55%, transparent); }
    .log-row time, .log-row small { color: var(--muted); font-size: .72rem; }
    .log-row pre { margin: 0; white-space: pre-wrap; word-break: break-word; font-family: inherit; }
    .log-row small { grid-column: 3; }
    .log-row.stderr { border-left: 2px solid var(--danger, #ff6b6b); }
    .level { font-size: .72rem; text-transform: uppercase; color: var(--muted); }
    .level[data-level='error'], .level[data-level='critical'] { color: var(--danger, #ff6b6b); }
    .level[data-level='warning'] { color: var(--warning, #f5c451); }
    .load-more { margin-top: 1rem; }
    @media (max-width: 850px) { .search-grid { grid-template-columns: 1fr 1fr; } .search-grid .wide { grid-column: 1 / -1; } .log-row { grid-template-columns: 1fr; } .log-row small { grid-column: 1; } }
  `]
})
export class ContainerLogsComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly route = inject(ActivatedRoute);

  readonly entries = signal<LogSearchEntryDto[]>([]);
  readonly loading = signal(false);
  readonly hasMore = signal(false);
  readonly paused = signal(false);
  readonly pausedCount = signal(0);
  readonly liveState = signal<'stopped' | 'connecting' | 'live' | 'reconnecting'>('stopped');
  readonly visibleEntries = computed(() => {
    const needle = this.viewFilter().trim().toLowerCase();
    return needle
      ? this.entries().filter((entry) => entry.rawLine.toLowerCase().includes(needle))
      : this.entries();
  });

  filters: LogSearchFilters = { text: '', level: '', stream: '', exceptionOnly: false, limit: 100 };
  fromLocal = '';
  toLocal = '';
  readonly viewFilter = signal('');
  cursor: string | null = null;
  liveSource: EventSource | null = null;

  async ngOnInit(): Promise<void> {
    this.fromLocal = this.toLocalInput(this.route.snapshot.queryParamMap.get('from'));
    this.toLocal = this.toLocalInput(this.route.snapshot.queryParamMap.get('to'));
    await this.search(true);
  }

  ngOnDestroy(): void {
    this.stopLive();
  }

  selectedContainerName(): string {
    return this.store.containers().find((container) => container.id === this.store.selectedContainerId())?.displayName ?? 'container';
  }

  async search(reset: boolean): Promise<void> {
    const containerId = this.requireContainerId();
    this.loading.set(true);
    this.store.setError('');
    try {
      const request: LogSearchFilters = {
        ...this.filters,
        from: this.toIso(this.fromLocal),
        to: this.toIso(this.toLocal),
        cursor: reset ? undefined : this.cursor ?? undefined
      };
      const response = await this.api.searchLogs(containerId, request);
      this.entries.set(reset ? response.items : [...this.entries(), ...response.items]);
      this.cursor = response.nextCursor;
      this.hasMore.set(response.hasMore);
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Log search failed.');
    } finally {
      this.loading.set(false);
    }
  }

  toggleLive(): void {
    if (this.liveSource) {
      this.stopLive();
    } else {
      this.startLive();
    }
  }

  togglePause(): void {
    if (!this.paused()) {
      this.paused.set(true);
      return;
    }

    this.paused.set(false);
    this.pausedCount.set(0);
    void this.search(true);
  }

  clear(): void {
    this.entries.set([]);
    this.cursor = null;
    this.hasMore.set(false);
  }

  downloadVisible(): void {
    const content = this.visibleEntries()
      .map((entry) => `${entry.timestamp}\t${entry.level || entry.stream}\t${entry.rawLine}`)
      .join('\n');
    const url = URL.createObjectURL(new Blob([content], { type: 'text/plain;charset=utf-8' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `tracebag-logs-${new Date().toISOString().replaceAll(':', '-')}.log`;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private startLive(): void {
    const containerId = this.requireContainerId();
    const afterId = Math.max(0, ...this.entries().map((entry) => entry.id));
    this.liveState.set('connecting');
    this.liveSource = new EventSource(`/api/containers/${encodeURIComponent(containerId)}/logs/live?afterId=${afterId}`);
    this.liveSource.addEventListener('open', () => this.liveState.set('live'));
    this.liveSource.addEventListener('log', (event) => {
      if (this.paused()) {
        this.pausedCount.update((count) => count + 1);
        return;
      }

      const entry = JSON.parse((event as MessageEvent<string>).data) as LogSearchEntryDto;
      if (!this.entries().some((current) => current.id === entry.id)) {
        this.entries.update((entries) => [entry, ...entries].slice(0, 3_000));
      }
    });
    this.liveSource.onerror = () => this.liveState.set('reconnecting');
  }

  private stopLive(): void {
    this.liveSource?.close();
    this.liveSource = null;
    this.liveState.set('stopped');
  }

  private requireContainerId(): string {
    const containerId = this.store.selectedContainerId();
    if (!containerId) {
      throw new Error('No container is selected.');
    }
    return containerId;
  }

  private toIso(value: string): string | undefined {
    return value ? new Date(value).toISOString() : undefined;
  }

  private toLocalInput(value: string | null): string {
    if (!value) return '';
    const date = new Date(value);
    const pad = (part: number) => String(part).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }
}
