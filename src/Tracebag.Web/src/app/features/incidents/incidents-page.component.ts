import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { ContainerDto, DotnetProcessDto, GuidedIncidentProfileDto, IncidentSummaryDto } from '../../tracebag.models';

@Component({
  selector: 'tb-incidents-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <section class="page-header">
      <div>
        <span class="prompt">Guided investigation</span>
        <h2>Incidents</h2>
        <p>Capture a bounded set of logs, metrics, and diagnostics around a serious production problem.</p>
      </div>
    </section>
    <section class="incident-grid">
      <section class="content-panel">
        <div class="panel-title"><span>New incident</span><small>Guided capture</small></div>
        <form class="incident-form" (ngSubmit)="create()">
          <label>Target container
            <select [(ngModel)]="containerId" name="containerId" (ngModelChange)="loadProcesses()" required>
              @for (container of containers(); track container.id) { <option [value]="container.id">{{ container.displayName }}</option> }
            </select>
          </label>
          <label>.NET process
            <select [(ngModel)]="processId" name="processId" required>
              @for (process of processes(); track process.pid) { <option [ngValue]="process.pid">{{ process.pid }} · {{ process.name }}</option> }
            </select>
          </label>
          <label>Guided profile
            <select [(ngModel)]="profileId" name="profileId" required>
              @for (profile of profiles(); track profile.id) { <option [value]="profile.id">{{ profile.displayName }}</option> }
            </select>
          </label>
          @if (selectedProfile(); as profile) {
            <p class="profile-help">{{ profile.description }}<br><code>{{ profile.counterPreset }}</code> + <code>{{ profile.primaryDiagnostic }}</code></p>
          }
          <label>Title <input [(ngModel)]="title" name="title" maxlength="200" placeholder="Optional incident title"></label>
          <label>What happened? <textarea [(ngModel)]="reason" name="reason" maxlength="2000" rows="3" placeholder="Symptoms, trigger, expected behavior"></textarea></label>
          <div class="form-row">
            <label>Capture seconds <input type="number" [(ngModel)]="captureSeconds" name="captureSeconds" min="10" max="120"></label>
            <label class="check"><input type="checkbox" [(ngModel)]="includeTrace" name="includeTrace"> Include bounded optional trace</label>
          </div>
          <button class="primary" type="submit" [disabled]="busy() || !processId">{{ busy() ? 'Starting capture…' : 'Start incident capture' }}</button>
          @if (error()) { <p class="error">{{ error() }}</p> }
        </form>
      </section>

      <section class="content-panel incident-list">
        <div class="panel-title"><span>Recent incidents</span><small>{{ incidents().length }} total</small><button type="button" class="compact" (click)="load()">Refresh</button></div>
        @if (!incidents().length) { <p class="empty-state">No incidents yet. Start with a guided capture.</p> }
        @for (incident of incidents(); track incident.id) {
          <a class="incident-row" [routerLink]="['/incidents', incident.id]">
            <span class="status-dot" [class.active]="isActive(incident.status)"></span>
            <span><strong>{{ incident.title }}</strong><small>{{ incident.containerName }} · {{ incident.profile }}</small></span>
            <span><code>{{ incident.status }}</code><small>{{ incident.progress }}%</small></span>
          </a>
        }
      </section>
    </section>
  `,
  styles: [`
    .incident-grid{display:grid;grid-template-columns:minmax(300px,420px) minmax(0,1fr);gap:14px}.incident-form{display:grid;gap:14px;padding:16px}.incident-form label{display:grid;gap:6px;color:var(--muted-strong);font-size:.8rem}.form-row{display:grid;grid-template-columns:1fr 1.5fr;gap:12px}.check{padding-top:22px}.profile-help{background:var(--accent-soft);border:1px solid rgb(94 234 212 / 18%);border-radius:var(--radius-sm);color:var(--muted-strong);font-size:.78rem;line-height:1.6;padding:11px}.incident-list{min-width:0}.incident-row{align-items:center;border-bottom:1px solid var(--line);color:inherit;display:grid;gap:12px;grid-template-columns:auto minmax(0,1fr) auto;padding:14px;text-decoration:none;transition:background-color 140ms ease}.incident-row:hover{background:var(--surface-hover)}.incident-row span{display:grid;gap:4px}.incident-row span:last-child{text-align:right}.incident-row small{color:var(--muted)}.status-dot.active{background:var(--success)}@media(max-width:850px){.incident-grid{grid-template-columns:1fr}.form-row{grid-template-columns:1fr}.check{padding-top:0}}
  `]
})
export class IncidentsPageComponent implements OnInit {
  private readonly api = inject(TracebagApiClient);
  private readonly router = inject(Router);
  containers = signal<ContainerDto[]>([]);
  processes = signal<DotnetProcessDto[]>([]);
  profiles = signal<GuidedIncidentProfileDto[]>([]);
  incidents = signal<IncidentSummaryDto[]>([]);
  busy = signal(false); error = signal('');
  containerId = ''; processId: number | null = null; profileId = 'frozen-api';
  title = ''; reason = ''; captureSeconds = 30; includeTrace = false;

  selectedProfile(): GuidedIncidentProfileDto | undefined { return this.profiles().find((x) => x.id === this.profileId); }
  isActive(status: string): boolean { return ['queued', 'collecting', 'analyzing'].includes(status); }

  async ngOnInit(): Promise<void> { await this.load(); }
  async load(): Promise<void> {
    this.error.set('');
    try {
      const [containers, profiles, incidents] = await Promise.all([this.api.containers(), this.api.incidentProfiles(), this.api.incidents()]);
      this.containers.set(containers); this.profiles.set(profiles); this.incidents.set(incidents);
      if (!this.containerId && containers.length) { this.containerId = containers[0].id; await this.loadProcesses(); }
    } catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not load incidents.'); }
  }
  async loadProcesses(): Promise<void> {
    this.processes.set([]); this.processId = null;
    if (!this.containerId) return;
    try { const processes = await this.api.dotnetProcesses(this.containerId); this.processes.set(processes); if (processes.length) this.processId = processes[0].pid; }
    catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not discover .NET processes.'); }
  }
  async create(): Promise<void> {
    if (!this.containerId || !this.processId) return;
    this.busy.set(true); this.error.set('');
    try {
      const result = await this.api.createIncident(this.containerId, { processId: this.processId, profile: this.profileId, title: this.title.trim() || null, reason: this.reason.trim() || null, captureSeconds: this.captureSeconds, includeTrace: this.includeTrace });
      await this.router.navigate(['/incidents', result.id]);
    } catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not create incident.'); }
    finally { this.busy.set(false); }
  }
}
