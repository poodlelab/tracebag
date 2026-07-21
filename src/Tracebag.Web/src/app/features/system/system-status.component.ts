import { CommonModule } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { SystemDependencyDto, SystemStatusDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { formatBytes } from '../../shared/tracebag-format';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'tb-system-status',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <section class="page-header">
      <div><span class="prompt">Installation health</span><h2>System status</h2><p>Tracebag services, storage, ingestion, and Docker connectivity at a glance.</p></div>
      <button class="secondary" type="button" (click)="load()"><tb-icon name="refresh" /> Refresh</button>
    </section>

    @if (status(); as current) {
      <section class="metric-grid">
        <article class="metric-card"><span>Active targets</span><strong>{{ current.activeTargetCount }}</strong><small>{{ current.knownTargetCount }} known identities</small></article>
        <article class="metric-card"><span>Event stream</span><strong>{{ current.eventCollector.status }}</strong><small>{{ current.eventCollector.retainedEventCount }} retained</small></article>
        <article class="metric-card"><span>Log ingestion</span><strong>{{ current.logIngestion.status }}</strong><small>{{ current.logIngestion.activeCollectors }} collectors · {{ current.logIngestion.queueDepth }}/{{ current.logIngestion.queueCapacity }} queued</small></article>
        <article class="metric-card"><span>Version</span><strong>{{ current.version }}</strong><small>started {{ current.startedAt | date: 'short' }}</small></article>
      </section>

      <section class="container-grid system-grid">
        @for (entry of dependencies(current); track entry.name) {
          <article class="content-panel">
            <div class="selected-header">
              <span class="status-dot" [class.ok]="entry.value.status === 'healthy'" [class.danger]="entry.value.status === 'unavailable'"></span>
              <div><strong>{{ entry.name }}</strong><small>{{ entry.value.status }}</small></div>
            </div>
            <p>{{ entry.value.message }}</p>
            <dl class="kv-list compact-kv">
              @for (detail of detailEntries(entry.value); track detail.key) {
                <div><dt>{{ detail.key }}</dt><dd>{{ detail.value }}</dd></div>
              }
            </dl>
          </article>
        }
      </section>

      <section class="content-panel">
        <div class="panel-title"><span>Docker event collector</span></div>
        <dl class="kv-list">
          <div><dt>Status</dt><dd>{{ current.eventCollector.status }}</dd></div>
          <div><dt>Last connected</dt><dd>{{ current.eventCollector.lastConnectedAt | date: 'medium' }}</dd></div>
          <div><dt>Last event</dt><dd>{{ current.eventCollector.lastEventAt | date: 'medium' }}</dd></div>
          @if (current.eventCollector.lastError) { <div><dt>Error</dt><dd>{{ current.eventCollector.lastError }}</dd></div> }
        </dl>
      </section>

      <section class="content-panel">
        <div class="panel-title"><span>Persistent log pipeline</span></div>
        <dl class="kv-list">
          <div><dt>Status</dt><dd>{{ current.logIngestion.status }}</dd></div>
          <div><dt>Storage</dt><dd>{{ current.logIngestion.storedEntries }} entries · {{ bytes(current.logIngestion.storedBytes) }}</dd></div>
          <div><dt>Queue</dt><dd>{{ current.logIngestion.queueDepth }} / {{ current.logIngestion.queueCapacity }}</dd></div>
          <div><dt>Persisted this run</dt><dd>{{ current.logIngestion.persistedEntries }}</dd></div>
          <div><dt>Dropped / duplicate</dt><dd>{{ current.logIngestion.droppedEntries }} / {{ current.logIngestion.duplicateEntries }}</dd></div>
          <div><dt>Retention deletions</dt><dd>{{ current.logIngestion.retentionDeletedEntries }}</dd></div>
          <div><dt>Ingestion lag</dt><dd>{{ current.logIngestion.ingestionLagSeconds === null ? '—' : (current.logIngestion.ingestionLagSeconds | number: '1.0-1') + ' s' }}</dd></div>
          <div><dt>Last persisted</dt><dd>{{ current.logIngestion.lastPersistedAt | date: 'medium' }}</dd></div>
          @if (current.logIngestion.lastError) { <div><dt>Notice</dt><dd>{{ current.logIngestion.lastError }}</dd></div> }
        </dl>
      </section>
    } @else {
      <section class="content-panel"><div class="empty-state rich-empty"><span class="spinner"></span><strong>Loading system status</strong></div></section>
    }
  `,
  styles: [`.system-grid { margin: 1rem 0; } dd { overflow-wrap: anywhere; }`]
})
export class SystemStatusComponent implements OnInit, OnDestroy {
  private readonly api = inject(TracebagApiClient);
  private readonly store = inject(TracebagStore);
  readonly status = signal<SystemStatusDto | null>(null);
  private timer: ReturnType<typeof setInterval> | null = null;
  bytes = formatBytes;

  async ngOnInit(): Promise<void> {
    await this.load();
    this.timer = setInterval(() => void this.load(false), 5000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  async load(showError = true): Promise<void> {
    try {
      this.status.set(await this.api.systemStatus());
      if (showError) this.store.setError('');
    } catch (error) {
      if (showError) this.store.setError(error instanceof Error ? error.message : 'System status unavailable.');
    }
  }

  dependencies(status: SystemStatusDto): { name: string; value: SystemDependencyDto }[] {
    return [
      { name: 'Docker Engine', value: status.docker },
      { name: 'PostgreSQL', value: status.database },
      { name: 'Artifact storage', value: status.artifactStorage },
      { name: 'Diagnostic runner', value: status.runnerImage },
      { name: 'Durable retention', value: status.dataRetention }
    ];
  }

  detailEntries(dependency: SystemDependencyDto): { key: string; value: unknown }[] {
    return Object.entries(dependency.details).map(([key, value]) => ({ key, value }));
  }
}
