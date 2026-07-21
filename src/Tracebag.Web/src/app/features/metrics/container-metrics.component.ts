import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { CounterMetricDto, CounterRecordingDto, LogEventDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { formatCounterValue } from '../../shared/tracebag-format';

@Component({
  selector: 'tb-container-metrics',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">metrics</span>
        <span class="workspace-target">{{ store.counterMetrics().length }} counters</span>
      </div>

      <div class="command-row">
        <button type="button" (click)="loadProcesses()">Discover</button>
        <label>process
          <select [ngModel]="store.selectedProcessId()" (ngModelChange)="store.setSelectedProcessId($event)">
            <option [ngValue]="null">select</option>
            @for (process of store.processes(); track process.pid) {
              <option [ngValue]="process.pid">{{ process.pid }} · {{ process.name }}</option>
            }
          </select>
        </label>
        <label>preset
          <select [(ngModel)]="counterPreset">
            <option value="runtime">Runtime</option>
            <option value="aspnet">ASP.NET</option>
            <option value="kestrel">Kestrel</option>
            <option value="thread-pool">Thread pool</option>
            <option value="gc-pressure">GC pressure</option>
            <option value="request-pressure">Request pressure</option>
            <option value="contention">Contention</option>
            <option value="all">Full web profile</option>
          </select>
        </label>
        <button type="button" [class.live]="store.counterSessionId()" [disabled]="!!store.counterSessionId()" (click)="startCounters()">Start</button>
        <button type="button" [disabled]="!store.counterSessionId()" (click)="stopCounters()">Stop</button>
      </div>

      <div class="counter-dashboard">
        <div class="counter-status">
          <span>{{ store.counterMetrics().length }} counters</span>
          @if (store.counterLastUpdatedAt()) {
            <span>updated {{ store.counterLastUpdatedAt() | date: 'mediumTime' }}</span>
          }
        </div>

        @if (counterHighlights().length) {
          <div class="metric-grid">
            @for (metric of counterHighlights(); track metric.id) {
              <article class="metric-card">
                <span>{{ metric.name }}</span>
                <strong>{{ formatCounterValue(metric) }}</strong>
                <small>{{ metric.provider }}</small>
              </article>
            }
          </div>
        } @else {
          <div class="empty-state rich-empty"><strong>No live counters</strong><span>Select a process and start the {{ counterPreset }} preset to begin monitoring.</span></div>
        }

        <div class="counter-table-wrap">
          <table class="counter-table">
            <thead>
              <tr>
                <th>Provider</th>
                <th>Counter</th>
                <th>Value</th>
                <th>Type</th>
                <th>Timestamp</th>
              </tr>
            </thead>
            <tbody>
              @for (metric of store.counterMetrics(); track metric.id) {
                <tr>
                  <td>{{ metric.provider }}</td>
                  <td>{{ metric.name }}</td>
                  <td class="metric-value">{{ formatCounterValue(metric) }}</td>
                  <td>{{ metric.counterType }}</td>
                  <td>{{ metric.timestamp }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <label class="check raw-toggle">
          <input type="checkbox" [(ngModel)]="showRawCounters">
          raw output
        </label>
        @if (showRawCounters) {
          <pre class="counter-raw">@for (entry of store.counterOutput(); track $index) {<span [class.stderr]="entry.stream === 'stderr'">{{ entry.line }}</span>
}</pre>
        }
      </div>
    </section>

    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">record</span>
        <span class="workspace-target">{{ containerRecordings().length }} sessions</span>
      </div>

      <div class="command-row">
        <label>name
          <input type="text" [(ngModel)]="recordingName" placeholder="Peak hour recording">
        </label>
        <label>interval
          <select [(ngModel)]="recordingIntervalSeconds">
            <option [ngValue]="2">2s</option>
            <option [ngValue]="5">5s</option>
            <option [ngValue]="10">10s</option>
          </select>
        </label>
        <label>max duration
          <select [(ngModel)]="recordingMaxDurationMinutes">
            <option [ngValue]="15">15m</option>
            <option [ngValue]="60">1h</option>
            <option [ngValue]="240">4h</option>
            <option [ngValue]="1440">24h</option>
          </select>
        </label>
        <button type="button" [disabled]="!!activeRecording()" (click)="startRecording()">Start Recording</button>
        <button type="button" [disabled]="!activeRecording()" (click)="stopRecording(activeRecording()!)">Stop Recording</button>
        <button type="button" (click)="loadRecordings()">Refresh</button>
      </div>

      @if (activeRecording(); as active) {
        <article class="recording-active">
          <div>
            <span class="chip active">recording</span>
            <h2>{{ active.name || active.containerName }}</h2>
            <p>{{ active.preset }} · every {{ active.intervalSeconds }}s · {{ active.sampleCount }} samples</p>
          </div>
          <a class="button-like" [routerLink]="['/recordings', active.id]">View</a>
        </article>
      }

      <div class="recording-list">
        @for (recording of containerRecordings(); track recording.id) {
          <article class="recording-row">
            <div>
              <strong>{{ recording.name || recording.containerName }}</strong>
              <span>{{ recording.status }} · {{ recording.preset }} · {{ recording.sampleCount }} samples</span>
            </div>
            <span>{{ recording.startedAt | date: 'short' }}</span>
            <div class="row-actions">
              <a class="button-like" [routerLink]="['/recordings', recording.id]">View</a>
              @if (isActiveRecording(recording)) {
                <button type="button" (click)="stopRecording(recording)">Stop</button>
              }
            </div>
          </article>
        } @empty {
          <div class="empty-state rich-empty"><strong>No recordings for this container</strong><span>Save a counter session to review it later.</span></div>
        }
      </div>
    </section>

  `
})
export class ContainerMetricsComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);

  readonly counterHighlights = computed(() => {
    const preferredNames = [
      'CPU Usage (%)',
      'Working Set (MB)',
      'GC Heap Size (MB)',
      'Allocation Rate (B / 1 sec)',
      'ThreadPool Queue Length',
      'Exception Count (Count / 1 sec)'
    ];
    const metrics = this.store.counterMetrics();
    return preferredNames
      .map((name) => metrics.find((metric) => metric.name === name))
      .filter((metric): metric is CounterMetricDto => !!metric);
  });

  counterPreset = 'runtime';
  recordingName = '';
  recordingIntervalSeconds = 5;
  recordingMaxDurationMinutes = 60;
  showRawCounters = false;
  formatCounterValue = formatCounterValue;
  private counterSource: EventSource | null = null;

  readonly containerRecordings = computed(() => {
    const containerId = this.store.selectedContainerId();
    return this.store.recordings().filter((recording) => recording.containerId === containerId);
  });

  readonly activeRecording = computed(() => {
    return this.containerRecordings().find((recording) => this.isActiveRecording(recording)) ?? null;
  });

  async ngOnInit(): Promise<void> {
    await this.loadRecordings();
  }

  ngOnDestroy(): void {
    if (this.store.counterSessionId()) {
      void this.stopCounters();
      return;
    }

    this.counterSource?.close();
  }

  async loadProcesses(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setProcesses(await this.api.dotnetProcesses(this.requireContainerId()));
    });
  }

  async startCounters(): Promise<void> {
    const containerId = this.requireContainerId();
    const processId = this.requireProcessId();
    await this.withLoading(async () => {
      const response = await this.api.startCounters(containerId, processId, this.counterPreset);
      this.store.setCounterSession(response.sessionId);
      this.counterSource?.close();
      this.counterSource = new EventSource(`/api/diagnostics/sessions/${response.sessionId}/stream`);
      this.counterSource.addEventListener('counter', (event) => {
        this.store.upsertCounterMetric(JSON.parse((event as MessageEvent<string>).data) as CounterMetricDto);
      });
      this.counterSource.addEventListener('log', (event) => {
        this.store.appendCounterOutput(JSON.parse((event as MessageEvent<string>).data) as LogEventDto);
      });
      this.counterSource.onerror = () => {
        this.store.setError('The live counter stream was disconnected.');
        this.counterSource?.close();
      };
    });
  }

  async stopCounters(): Promise<void> {
    const sessionId = this.store.counterSessionId();
    if (!sessionId) {
      return;
    }

    this.counterSource?.close();
    this.counterSource = null;
    await this.api.stopSession(sessionId);
    this.store.setError('');
    this.store.stopCounterSession();
  }

  async loadRecordings(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setRecordings(await this.api.recordings(undefined, this.requireContainerId()));
    });
  }

  async startRecording(): Promise<void> {
    const containerId = this.requireContainerId();
    const processId = this.requireProcessId();
    await this.withLoading(async () => {
      await this.api.startRecording(
        containerId,
        processId,
        this.counterPreset,
        this.recordingIntervalSeconds,
        this.recordingMaxDurationMinutes,
        this.recordingName);
      this.recordingName = '';
      await this.loadRecordings();
    });
  }

  async stopRecording(recording: CounterRecordingDto): Promise<void> {
    await this.withLoading(async () => {
      const stopped = await this.api.stopRecording(recording.id);
      this.store.upsertRecording(stopped);
      await this.loadRecordings();
    });
  }

  isActiveRecording(recording: CounterRecordingDto): boolean {
    return recording.status === 'starting' || recording.status === 'running' || recording.status === 'stopping';
  }

  private requireContainerId(): string {
    const containerId = this.store.selectedContainerId();
    if (!containerId) {
      throw new Error('No container is selected.');
    }

    return containerId;
  }

  private requireProcessId(): number {
    const processId = this.store.selectedProcessId();
    if (!processId) {
      throw new Error('No .NET process is selected.');
    }

    return processId;
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try {
      await work();
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'An unexpected error occurred.');
    } finally {
      this.store.setLoading(false);
    }
  }
}
