import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { ContainerOverviewDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { shortId, stateClass } from '../../shared/tracebag-format';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  selector: 'tb-container-overview',
  standalone: true,
  imports: [CommonModule, RouterLink, ConfirmDialogComponent],
  template: `
    @if (overview(); as data) {
      <section class="overview-grid">
        <article class="content-panel">
          <div class="panel-title">
            <span>identity & instance</span>
            <button type="button" (click)="loadOverview()">Refresh</button>
          </div>
          <dl class="kv-list">
            <div><dt>Logical ID</dt><dd>{{ data.container.id }}</dd></div>
            <div><dt>Docker ID</dt><dd>{{ shortId(data.container.dockerId) }}</dd></div>
            <div><dt>Identity source</dt><dd>{{ data.container.identitySource }}</dd></div>
            <div><dt>Known instances</dt><dd>{{ data.knownInstanceCount }}</dd></div>
            <div><dt>Image</dt><dd>{{ data.container.image }}</dd></div>
            <div><dt>Platform</dt><dd>{{ data.inspect.platform }} · {{ data.inspect.driver }}</dd></div>
            <div><dt>Started</dt><dd>{{ data.inspect.startedAt | date: 'short' }}</dd></div>
          </dl>
        </article>

        <article class="content-panel">
          <div class="panel-title"><span>runtime state</span></div>
          <div class="metric-grid operational-metrics">
            <div class="metric-card">
              <span>Health</span>
              <strong [class]="stateClass(data.inspect.health.status)">{{ data.inspect.health.status }}</strong>
              <small>failing streak {{ data.inspect.health.failingStreak }}</small>
            </div>
            <div class="metric-card">
              <span>Restarts</span>
              <strong>{{ data.inspect.restartCount }}</strong>
              <small>exit {{ data.inspect.exitCode }}</small>
            </div>
            <div class="metric-card">
              <span>OOM killed</span>
              <strong [class.danger]="data.inspect.oomKilled">{{ data.inspect.oomKilled ? 'yes' : 'no' }}</strong>
              <small>PID {{ data.inspect.pid }}</small>
            </div>
          </div>
        </article>

        <article class="content-panel resources-panel">
          <div class="panel-title">
            <span>docker resources</span>
            @if (data.resources.readAt) { <small>{{ data.resources.readAt | date: 'mediumTime' }}</small> }
          </div>
          @if (data.resources.available) {
            <div class="metric-grid operational-metrics">
              <div class="metric-card"><span>CPU</span><strong>{{ number(data.resources.cpuPercent) }}%</strong></div>
              <div class="metric-card"><span>Memory</span><strong>{{ bytes(data.resources.memoryUsageBytes) }}</strong><small>{{ number(data.resources.memoryPercent) }}% of {{ bytes(data.resources.memoryLimitBytes) }}</small></div>
              <div class="metric-card"><span>Network</span><strong>↓ {{ bytes(data.resources.networkRxBytes) }}</strong><small>↑ {{ bytes(data.resources.networkTxBytes) }}</small></div>
              <div class="metric-card"><span>Block I/O</span><strong>R {{ bytes(data.resources.blockReadBytes) }}</strong><small>W {{ bytes(data.resources.blockWriteBytes) }}</small></div>
              <div class="metric-card"><span>Processes</span><strong>{{ data.resources.pids ?? '—' }}</strong></div>
            </div>
          } @else {
            <p class="notice">{{ data.resources.unavailableReason }}</p>
          }
        </article>

        <article class="content-panel">
          <div class="panel-title"><span>actions</span></div>
          <div class="action-grid">
            <a class="button-like" routerLink="../logs">Open logs</a>
            @if (data.container.kind === 'dotnet') {
              <a class="button-like" routerLink="../metrics">Open metrics</a>
              <a class="button-like" routerLink="../diagnostics">Run diagnostics</a>
            }
            <a class="button-like" routerLink="../artifacts">Artifacts</a>
            @if (data.container.restartAllowed) {
              <button class="danger" type="button" (click)="confirmRestart.set(true)">Restart</button>
            }
          </div>
        </article>

        <article class="content-panel resources-panel">
          <div class="panel-title"><span>docker event timeline</span></div>
          <div class="recording-list">
            @for (event of data.recentEvents; track event.id || event.timestamp + event.action) {
              <div class="recording-row">
                <strong>{{ event.action }}</strong>
                <span>{{ event.timestamp | date: 'medium' }}</span>
                <small>{{ shortId(event.dockerId) }}</small>
              </div>
            } @empty {
              <p class="empty-state">No recent Docker events.</p>
            }
          </div>
        </article>

        @if (data.inspect.health.recentChecks.length) {
          <article class="content-panel resources-panel">
            <div class="panel-title"><span>healthcheck output</span></div>
            @for (check of data.inspect.health.recentChecks; track check.endedAt) {
              <pre class="counter-raw">exit {{ check.exitCode }} · {{ check.endedAt | date: 'mediumTime' }}\n{{ check.output }}</pre>
            }
          </article>
        }
      </section>
      @if (confirmRestart()) {
        <tb-confirm-dialog
          title="Restart container?"
          [message]="'This will restart ' + (selectedContainer()?.displayName || 'the selected container') + ' and briefly interrupt the service.'"
          confirmLabel="Restart container"
          (cancelled)="confirmRestart.set(false)"
          (confirmed)="restart()" />
      }
    } @else {
      <section class="content-panel"><div class="empty-state rich-empty"><span class="spinner"></span><strong>Loading container details</strong></div></section>
    }
  `,
  styles: [`
    .resources-panel { grid-column: 1 / -1; }
    .operational-metrics { margin-top: 1rem; }
    .danger { color: var(--danger, #ff6b6b); }
    .recording-row { grid-template-columns: 1fr auto auto; }
    dd { overflow-wrap: anywhere; }
  `]
})
export class ContainerOverviewComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  readonly overview = signal<ContainerOverviewDto | null>(null);
  readonly confirmRestart = signal(false);
  private refreshTimer: ReturnType<typeof setInterval> | null = null;

  readonly selectedContainer = computed(() =>
    this.store.containers().find((container) => container.id === this.store.selectedContainerId()) ?? null
  );

  shortId = shortId;
  stateClass = stateClass;

  async ngOnInit(): Promise<void> {
    await this.loadOverview();
    this.refreshTimer = setInterval(() => void this.loadOverview(false), 3000);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) {
      clearInterval(this.refreshTimer);
    }
  }

  async loadOverview(showError = true): Promise<void> {
    const containerId = this.store.selectedContainerId();
    if (!containerId) return;
    try {
      this.overview.set(await this.api.containerOverview(containerId));
      if (showError) this.store.setError('');
    } catch (error) {
      if (showError) this.store.setError(error instanceof Error ? error.message : 'Operational data unavailable.');
    }
  }

  async restart(): Promise<void> {
    this.confirmRestart.set(false);
    const container = this.selectedContainer();
    if (!container?.restartAllowed) return;
    await this.api.restart(container.id);
    this.store.setContainers(await this.api.containers());
    await this.loadOverview();
  }

  bytes(value: number | null): string {
    if (value === null) return '—';
    const units = ['B', 'KiB', 'MiB', 'GiB', 'TiB'];
    let scaled = value;
    let unit = 0;
    while (scaled >= 1024 && unit < units.length - 1) { scaled /= 1024; unit++; }
    return `${scaled.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
  }

  number(value: number | null): string {
    return value === null ? '—' : value.toFixed(1);
  }
}
