import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { CounterRecordingDto, CounterSeriesDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  selector: 'tb-recording-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, ConfirmDialogComponent],
  template: `
    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">recording</span>
        @if (recording(); as current) {
          <span class="workspace-target">{{ current.status }} · {{ current.sampleCount }} raw samples · .NET {{ current.runtimeMajor }}</span>
        }
      </div>

      @if (recording(); as current) {
        <div class="recording-hero">
          <div>
            <span class="chip active">{{ current.preset }}</span>
            <h2>{{ current.name || current.containerName }}</h2>
            <p>{{ current.containerName }} · pid {{ current.processId }} · every {{ current.intervalSeconds }}s</p>
          </div>
          <div class="row-actions">
            <a class="button-like" [routerLink]="['/containers', current.containerId, 'metrics']">Live metrics</a>
            @if (selectedTimestamp()) {
              <a class="button-like" [routerLink]="['/containers', current.containerId, 'logs']" [queryParams]="logWindow()">Logs at cursor</a>
            }
            <a class="button-like" [href]="api.recordingExportUrl(current.id, 'csv')">Export CSV</a>
            <a class="button-like" [href]="api.recordingExportUrl(current.id, 'json')">Export JSON</a>
            @if (isActive(current)) {
              <button type="button" (click)="stop(current)">Stop</button>
            }
            <button class="danger" type="button" [disabled]="isActive(current)" (click)="confirmDelete.set(true)">Delete</button>
          </div>
        </div>

        <dl class="detail-grid">
          <div><dt>Started</dt><dd>{{ current.startedAt | date: 'medium' }}</dd></div>
          <div><dt>Stopped</dt><dd>{{ current.stoppedAt ? (current.stoppedAt | date: 'medium') : 'running' }}</dd></div>
          <div><dt>Last sample</dt><dd>{{ current.lastSampleAt ? (current.lastSampleAt | date: 'medium') : 'none' }}</dd></div>
          <div><dt>Stop reason</dt><dd>{{ current.stopReason || '-' }}</dd></div>
          <div><dt>Runner</dt><dd>.NET {{ current.runtimeMajor }} · tools {{ current.toolVersion }}</dd></div>
          <div><dt>Image</dt><dd>{{ current.runnerImage }}</dd></div>
        </dl>

        <form class="recording-notes" (ngSubmit)="saveMetadata(current)">
          <label>Name <input name="name" maxlength="160" [(ngModel)]="editableName"></label>
          <label>Investigation notes <textarea name="notes" maxlength="4000" rows="3" [(ngModel)]="editableNotes" placeholder="What happened, what changed, and what should be checked next?"></textarea></label>
          <button type="submit">Save notes</button>
        </form>

        @if (current.errorMessage) { <p class="notice">{{ current.errorMessage }}</p> }
      } @else {
        <div class="empty-state rich-empty"><span class="spinner small"></span><strong>Loading recording</strong></div>
      }
    </section>

    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">historical profile</span>
        <span class="workspace-target">{{ visibleSeries().length }} synchronized series</span>
      </div>

      <div class="command-row">
        <label>resolution
          <select [(ngModel)]="resolution" (ngModelChange)="loadSamples()">
            <option value="auto">Auto</option>
            <option value="raw">Raw</option>
            <option value="1m">1 minute</option>
          </select>
        </label>
        <span>using {{ store.recordingSamples()?.resolution || '…' }}</span>
        @if (store.recordingSamples()?.truncated) { <span class="notice">Result capped; choose 1 minute.</span> }
        @if (selectedTimestamp()) {
          <span>cursor {{ selectedTimestamp() | date: 'medium' }}</span>
          <button type="button" (click)="selectedTimestamp.set(null)">Clear cursor</button>
        }
      </div>

      @if (visibleSeries().length) {
        <div class="graph-grid">
          @for (series of visibleSeries(); track series.provider + series.name + series.counterType) {
            <article class="graph-card">
              <div class="graph-title">
                <strong>{{ series.name }}</strong>
                <span>{{ series.provider }}</span>
              </div>
              <button class="peak-button" type="button" (click)="selectPeak(series)">peak {{ number(series.summary.maximum) }} at {{ series.summary.peakAt | date: 'shortTime' }}</button>
              <svg viewBox="0 0 360 110" preserveAspectRatio="none" role="img" (pointermove)="moveCursor($event)" (pointerleave)="hovering.set(false)" (pointerenter)="hovering.set(true)">
                <polyline [attr.points]="polyline(series)" />
                @if (selectedTimestamp()) { <line class="time-cursor" [attr.x1]="cursorX()" [attr.x2]="cursorX()" y1="0" y2="110" /> }
              </svg>
              <div class="graph-summary">
                <span>min <strong>{{ number(series.summary.minimum) }}</strong></span>
                <span>avg <strong>{{ number(series.summary.average) }}</strong></span>
                <span>max <strong>{{ number(series.summary.maximum) }}</strong></span>
                <span>n <strong>{{ series.summary.sampleCount }}</strong></span>
              </div>
              @if (selectedTimestamp()) {
                <div class="cursor-value">at cursor <strong>{{ number(valueAtCursor(series)) }}</strong></div>
              }
            </article>
          }
        </div>
      } @else {
        <div class="empty-state rich-empty"><strong>No samples captured</strong><span>This recording does not contain counter samples yet.</span></div>
      }
    </section>

    <section class="content-panel route-panel">
      <div class="workspace-tabs">
        <span class="prompt">series inventory</span>
        <span class="workspace-target">{{ store.recordingDetail()?.series?.length || 0 }} counters</span>
      </div>
      <div class="counter-table-wrap">
        <table class="counter-table">
          <thead><tr><th>Provider</th><th>Counter</th><th>Samples</th><th>First</th><th>Last</th></tr></thead>
          <tbody>
            @for (series of store.recordingDetail()?.series || []; track series.provider + series.name + series.counterType) {
              <tr><td>{{ series.provider }}</td><td>{{ series.name }}</td><td>{{ series.sampleCount }}</td><td>{{ series.firstSampleAt | date: 'shortTime' }}</td><td>{{ series.lastSampleAt | date: 'shortTime' }}</td></tr>
            }
          </tbody>
        </table>
      </div>
    </section>

    @if (confirmDelete() && recording(); as current) {
      <tb-confirm-dialog
        title="Delete recording?"
        [message]="'This permanently removes ' + (current.name || current.id) + ' and all captured samples.'"
        confirmLabel="Delete recording"
        (cancelled)="confirmDelete.set(false)"
        (confirmed)="delete(current)" />
    }
  `,
  styles: [`
    .recording-notes { border-top: 1px solid var(--line); display: grid; gap: .75rem; margin-top: 0; padding: 1rem; }
    .recording-notes label { display: grid; gap: .35rem; color: var(--muted); }
    .recording-notes textarea { resize: vertical; }
    .peak-button { width: fit-content; margin: .35rem 0; padding: .25rem .45rem; font-size: .72rem; }
    .graph-summary { display: grid; grid-template-columns: repeat(4, 1fr); gap: .35rem; font-size: .72rem; color: var(--muted); }
    .graph-summary span { display: grid; }
    .graph-summary strong, .cursor-value strong { color: var(--text); }
    .cursor-value { margin-top: .45rem; color: var(--accent); font-size: .8rem; }
    .time-cursor { stroke: var(--warning, #f5c451); stroke-width: 1.5; vector-effect: non-scaling-stroke; }
  `]
})
export class RecordingDetailComponent implements OnInit {
  readonly store = inject(TracebagStore);
  readonly api = inject(TracebagApiClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly selectedTimestamp = signal<string | null>(null);
  readonly hovering = signal(false);
  readonly confirmDelete = signal(false);
  resolution = 'auto';
  editableName = '';
  editableNotes = '';

  readonly recording = computed(() => this.store.recordingDetail()?.recording ?? null);
  readonly visibleSeries = computed(() => {
    const preferred = [
      'cpu-usage', 'CPU Usage (%)', 'working-set', 'Working Set (MB)',
      'gc-heap-size', 'GC Heap Size (MB)', 'alloc-rate', 'Allocation Rate (B / 1 sec)',
      'threadpool-queue-length', 'ThreadPool Queue Length', 'monitor-lock-contention-count',
      'Monitor Lock Contention Count', 'current-requests', 'requests-per-second', 'failed-requests',
      'http.server.active_requests', 'http.server.request.duration',
      'current-connections', 'kestrel.active_connections', 'kestrel.queued_connections'
    ];
    const series = this.store.recordingSamples()?.series ?? [];
    const picked = preferred
      .map((name) => series.find((entry) => entry.name === name || entry.name.startsWith(name)))
      .filter((entry, index, selected): entry is CounterSeriesDto => !!entry && selected.indexOf(entry) === index);
    return (picked.length ? picked : series).slice(0, 10);
  });

  async ngOnInit(): Promise<void> { await this.load(); }

  async stop(recording: CounterRecordingDto): Promise<void> {
    await this.withLoading(async () => { await this.api.stopRecording(recording.id); await this.load(); });
  }

  async saveMetadata(recording: CounterRecordingDto): Promise<void> {
    await this.withLoading(async () => {
      await this.api.updateRecording(recording.id, this.editableName, this.editableNotes);
      await this.loadDetail(recording.id);
    });
  }

  async delete(recording: CounterRecordingDto): Promise<void> {
    this.confirmDelete.set(false);
    await this.withLoading(async () => {
      await this.api.deleteRecording(recording.id);
      this.store.setRecordingDetail(null);
      this.store.setRecordingSamples(null);
      await this.router.navigateByUrl('/recordings');
    });
  }

  isActive(recording: CounterRecordingDto): boolean {
    return recording.status === 'starting' || recording.status === 'running' || recording.status === 'stopping';
  }

  polyline(series: CounterSeriesDto): string {
    if (!series.points.length) return '';
    const min = series.summary.minimum;
    const span = series.summary.maximum - min || 1;
    const range = this.timeRange();
    return series.points.map((point) => {
      const x = range.end === range.start ? 0 : ((new Date(point.timestamp).getTime() - range.start) / (range.end - range.start)) * 360;
      const y = 105 - ((point.value - min) / span) * 100;
      return `${x.toFixed(1)},${y.toFixed(1)}`;
    }).join(' ');
  }

  moveCursor(event: PointerEvent): void {
    const rect = (event.currentTarget as SVGElement).getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (event.clientX - rect.left) / Math.max(1, rect.width)));
    const range = this.timeRange();
    this.selectedTimestamp.set(new Date(range.start + ((range.end - range.start) * ratio)).toISOString());
  }

  cursorX(): number {
    const selected = this.selectedTimestamp();
    if (!selected) return 0;
    const range = this.timeRange();
    return range.end === range.start ? 0 : ((new Date(selected).getTime() - range.start) / (range.end - range.start)) * 360;
  }

  selectPeak(series: CounterSeriesDto): void { this.selectedTimestamp.set(series.summary.peakAt); }

  valueAtCursor(series: CounterSeriesDto): number {
    const selected = new Date(this.selectedTimestamp() || series.summary.peakAt).getTime();
    return series.points.reduce((closest, point) =>
      Math.abs(new Date(point.timestamp).getTime() - selected) < Math.abs(new Date(closest.timestamp).getTime() - selected) ? point : closest
    ).value;
  }

  logWindow(): { from: string; to: string } {
    const center = new Date(this.selectedTimestamp() || Date.now()).getTime();
    return { from: new Date(center - 120_000).toISOString(), to: new Date(center + 120_000).toISOString() };
  }

  number(value: number): string { return value.toLocaleString(undefined, { maximumFractionDigits: 2 }); }

  async loadSamples(): Promise<void> {
    const recordingId = this.route.snapshot.paramMap.get('id');
    if (!recordingId) return;
    await this.withLoading(async () => this.store.setRecordingSamples(await this.api.recordingSamples(recordingId, this.resolution)));
  }

  private async load(): Promise<void> {
    const recordingId = this.route.snapshot.paramMap.get('id');
    if (!recordingId) return;
    await this.withLoading(async () => {
      await Promise.all([this.loadDetail(recordingId), this.loadSamplesWithoutState(recordingId)]);
    });
  }

  private async loadDetail(recordingId: string): Promise<void> {
    const detail = await this.api.recording(recordingId);
    this.store.setRecordingDetail(detail);
    this.editableName = detail.recording.name || '';
    this.editableNotes = detail.recording.notes || '';
  }

  private async loadSamplesWithoutState(recordingId: string): Promise<void> {
    this.store.setRecordingSamples(await this.api.recordingSamples(recordingId, this.resolution));
  }

  private timeRange(): { start: number; end: number } {
    const points = this.visibleSeries().flatMap((series) => series.points);
    if (!points.length) return { start: 0, end: 1 };
    return {
      start: Math.min(...points.map((point) => new Date(point.timestamp).getTime())),
      end: Math.max(...points.map((point) => new Date(point.timestamp).getTime()))
    };
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try { await work(); }
    catch (error) { this.store.setError(error instanceof Error ? error.message : 'Unexpected error.'); }
    finally { this.store.setLoading(false); }
  }
}
