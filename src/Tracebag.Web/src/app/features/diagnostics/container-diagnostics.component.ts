import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { DiagnosticJobDto, DiagnosticJobEventDto, DiagnosticJobProfileDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';

const fullDumpConfirmation = 'I understand this full dump may contain secrets and personal data';
const activeStatuses = new Set(['queued', 'validating', 'starting', 'running', 'collecting', 'stopping']);

@Component({
  selector: 'tb-container-diagnostics',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="diagnostics-grid">
      <article class="content-panel diag-block">
        <div class="panel-title">
          <span>process</span>
          <button type="button" (click)="loadProcesses()">Discover</button>
        </div>
        <label>managed process
          <select [ngModel]="store.selectedProcessId()" (ngModelChange)="store.setSelectedProcessId($event)">
            <option [ngValue]="null">Select a process</option>
            @for (process of store.processes(); track process.pid) {
              <option [ngValue]="process.pid">{{ process.pid }} · {{ process.name }}</option>
            }
          </select>
        </label>
        @if (store.processes().length) {
          <div class="process-list">
            @for (process of store.processes(); track process.pid) {
              <div><strong>{{ process.pid }} · {{ process.name }}</strong><small>{{ process.commandLine }}</small></div>
            }
          </div>
        } @else {
          <div class="empty-state rich-empty"><strong>No process selected</strong><span>Discover the managed processes in this container to continue.</span></div>
        }
      </article>

      <article class="content-panel diag-block capture-panel">
        <div class="panel-title"><span>new capture</span><small>server-owned profile</small></div>
        <label>profile
          <select [(ngModel)]="selectedProfileId" (ngModelChange)="selectProfile($event)">
            @for (profile of profiles; track profile.id) {
              <option [value]="profile.id" [disabled]="!profile.enabled">
                {{ profile.displayName }}{{ profile.enabled ? '' : ' · disabled' }}
              </option>
            }
          </select>
        </label>
        @if (selectedProfile(); as profile) {
          <p>{{ profile.description }}</p>
          @if (profile.defaultDurationSeconds > 0) {
            <label>duration · max {{ profile.maxDurationSeconds }}s
              <input type="number" min="1" [max]="profile.maxDurationSeconds" [(ngModel)]="durationSeconds">
            </label>
          }
          @if (profile.sensitive) {
            <div class="diagnostic-warning">
              Full dumps can contain credentials, request bodies and personal data. The installation and target container must both opt in.
            </div>
            <label class="check">
              <input type="checkbox" [(ngModel)]="fullDumpConfirmed">
              I understand the data exposure risk.
            </label>
          }
          <button type="button" [class.danger]="profile.sensitive" [disabled]="!profile.enabled || store.loading()" (click)="startCapture()">
            Start {{ profile.displayName }}
          </button>
        }
      </article>
    </section>

    <section class="content-panel job-console">
      <div class="panel-title">
        <span>diagnostic jobs</span>
        <button type="button" (click)="loadJobs()">Refresh</button>
      </div>
      @for (job of visibleJobs(); track job.id) {
        <article class="diagnostic-job">
          <div class="job-heading">
            <div>
              <strong>{{ profileName(job.profile) }}</strong>
              <small>{{ job.id }} · PID {{ job.processId }} · .NET {{ job.runtimeMajor }}</small>
            </div>
            <span class="status-pill" [class]="'status-pill ' + job.status">{{ job.status }}</span>
          </div>
          <progress max="100" [value]="job.progress"></progress>
          <p>{{ job.statusMessage }}</p>
          @if (job.errorMessage) { <p class="diagnostic-error">{{ job.errorCode }} · {{ job.errorMessage }}</p> }
          <div class="job-meta">
            <small>{{ job.createdAt | date: 'medium' }} · {{ job.runnerImage }} · tools {{ job.toolVersion }}</small>
            <div>
              @if (isActive(job)) { <button type="button" class="danger" (click)="cancel(job)">Cancel</button> }
              @if (job.artifactId) { <a class="button-like" [href]="'/api/artifacts/' + job.artifactId + '/download'">Download</a> }
            </div>
          </div>
          @if (events[job.id]?.length) {
            <details>
              <summary>{{ events[job.id].length }} lifecycle events</summary>
              <ol class="job-events">
                @for (event of events[job.id]; track event.id) {
                  <li><time>{{ event.timestamp | date: 'mediumTime' }}</time><span>{{ event.status }} · {{ event.message }}</span></li>
                }
              </ol>
            </details>
          }
        </article>
      } @empty {
        <div class="empty-state rich-empty"><strong>No diagnostic captures</strong><span>Choose a process and profile above to collect your first snapshot.</span></div>
      }
    </section>

  `
})
export class ContainerDiagnosticsComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly streams = new Map<string, EventSource>();
  private refreshTimer: ReturnType<typeof setInterval> | null = null;

  profiles: DiagnosticJobProfileDto[] = [];
  selectedProfileId = 'stack-snapshot';
  durationSeconds = 30;
  fullDumpConfirmed = false;
  events: Record<string, DiagnosticJobEventDto[]> = {};

  async ngOnInit(): Promise<void> {
    await this.withLoading(async () => {
      this.profiles = await this.api.diagnosticProfiles();
      this.selectProfile(this.profiles.find(profile => profile.enabled)?.id ?? 'stack-snapshot');
      await Promise.all([this.loadProcesses(), this.loadJobs()]);
    });
    this.refreshTimer = setInterval(() => void this.loadJobs(false), 3000);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer) clearInterval(this.refreshTimer);
    this.streams.forEach(stream => stream.close());
  }

  async loadProcesses(): Promise<void> {
    try { this.store.setProcesses(await this.api.dotnetProcesses(this.requireContainerId())); }
    catch (error) { this.store.setError(this.message(error)); }
  }

  async loadJobs(showError = true): Promise<void> {
    try {
      const jobs = await this.api.diagnosticJobs(this.requireContainerId());
      this.store.setDiagnosticJobs(jobs);
      jobs.filter(job => this.isActive(job)).forEach(job => this.connect(job.id));
    } catch (error) {
      if (showError) this.store.setError(this.message(error));
    }
  }

  selectProfile(profileId: string): void {
    this.selectedProfileId = profileId;
    const profile = this.selectedProfile();
    this.durationSeconds = profile?.defaultDurationSeconds || 30;
    this.fullDumpConfirmed = false;
  }

  selectedProfile(): DiagnosticJobProfileDto | undefined {
    return this.profiles.find(profile => profile.id === this.selectedProfileId);
  }

  async startCapture(): Promise<void> {
    await this.withLoading(async () => {
      const profile = this.selectedProfile();
      if (!profile) throw new Error('Select a diagnostic profile.');
      if (profile.sensitive && !this.fullDumpConfirmed) throw new Error('Confirm the full-dump data exposure risk first.');
      const job = await this.api.startDiagnosticJob(
        this.requireContainerId(), this.requireProcessId(), profile.id,
        profile.defaultDurationSeconds > 0 ? this.durationSeconds : null,
        profile.sensitive ? fullDumpConfirmation : null);
      this.store.upsertDiagnosticJob(job);
      this.connect(job.id);
    });
  }

  async cancel(job: DiagnosticJobDto): Promise<void> {
    try { this.store.upsertDiagnosticJob(await this.api.cancelDiagnosticJob(job.id)); }
    catch (error) { this.store.setError(this.message(error)); }
  }

  visibleJobs(): DiagnosticJobDto[] {
    return this.store.diagnosticJobs().filter(job => job.containerId === this.store.selectedContainerId());
  }

  isActive(job: DiagnosticJobDto): boolean { return activeStatuses.has(job.status); }
  profileName(id: string): string { return this.profiles.find(profile => profile.id === id)?.displayName ?? id; }

  private connect(jobId: string): void {
    if (this.streams.has(jobId)) return;
    const source = new EventSource(`/api/diagnostic-jobs/${encodeURIComponent(jobId)}/events`);
    this.streams.set(jobId, source);
    const receive = (message: MessageEvent<string>) => {
      const event = JSON.parse(message.data) as DiagnosticJobEventDto;
      this.events = { ...this.events, [jobId]: [...(this.events[jobId] ?? []), event].slice(-100) };
      void this.api.diagnosticJob(jobId).then(job => {
        this.store.upsertDiagnosticJob(job);
        if (!this.isActive(job)) {
          source.close();
          this.streams.delete(jobId);
          if (job.artifactId) void this.api.artifacts().then(artifacts => this.store.setArtifacts(artifacts));
        }
      });
    };
    source.addEventListener('state', receive as EventListener);
    source.addEventListener('completed', receive as EventListener);
    source.onerror = () => {
      if (!this.isActive(this.store.diagnosticJobs().find(job => job.id === jobId) ?? {} as DiagnosticJobDto)) {
        source.close();
        this.streams.delete(jobId);
      }
    };
  }

  private requireContainerId(): string {
    const value = this.store.selectedContainerId();
    if (!value) throw new Error('Select a container first.');
    return value;
  }

  private requireProcessId(): number {
    const value = this.store.selectedProcessId();
    if (!value) throw new Error('Select a .NET process first.');
    return value;
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try { await work(); }
    catch (error) { this.store.setError(this.message(error)); }
    finally { this.store.setLoading(false); }
  }

  private message(error: unknown): string { return error instanceof Error ? error.message : 'Unexpected diagnostic error.'; }
}
