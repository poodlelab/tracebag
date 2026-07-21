import { CommonModule } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { IncidentDetailDto, IncidentEvidenceDto } from '../../tracebag.models';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  selector: 'tb-incident-detail', standalone: true, imports: [CommonModule, FormsModule, RouterLink, ConfirmDialogComponent],
  template: `
    <section class="incident-detail">
      <a routerLink="/incidents" class="back-link">← incidents</a>
      @if (detail(); as data) {
        <header class="terminal-panel incident-head">
          <div><span class="prompt">{{ data.incident.id }}</span><h2>{{ data.incident.title }}</h2><p>{{ data.incident.reason || 'No trigger note supplied.' }}</p></div>
          <div class="head-status"><span class="chip active">{{ data.incident.status }}</span><strong>{{ data.incident.progress }}%</strong><small>{{ data.incident.containerName }} · PID {{ data.incident.processId }}</small></div>
          <div class="progress"><span [style.width.%]="data.incident.progress"></span></div>
          @if (data.incident.errorMessage) { <p class="warning">{{ data.incident.errorMessage }}</p> }
        </header>

        <section class="evidence-layout">
          <section class="terminal-panel">
            <div class="panel-title"><span>correlated capture</span><strong>Timeline</strong></div>
            <div class="timeline">
              @for (event of data.timeline; track event.id) {
                <button type="button" class="timeline-event" [class.selected]="event.evidenceId === selectedEvidenceId()" (click)="selectEvidence(event.evidenceId)">
                  <time>{{ event.timestamp | date:'HH:mm:ss' }}</time><span [class]="event.severity"><strong>{{ event.title }}</strong><small>{{ event.summary }}</small></span>
                </button>
              }
            </div>
          </section>
          <section class="terminal-panel">
            <div class="panel-title"><span>{{ data.evidence.length }} bounded sources</span><strong>Evidence</strong></div>
            <div class="evidence-list">
              @for (evidence of data.evidence; track evidence.id) {
                <article [id]="evidence.id" class="evidence-card" [class.selected]="evidence.id === selectedEvidenceId()">
                  <div><span class="chip">{{ evidence.kind }}</span><span class="chip" [class.sensitive]="evidence.sensitive">{{ evidence.redactionStatus }}</span></div>
                  <h3>{{ evidence.title }}</h3><code>{{ evidence.id }}</code>
                  <pre>{{ evidence.summary | json }}</pre>
                  @if (evidence.artifactId) { <a [href]="'/api/artifacts/' + evidence.artifactId + '/download'">Download artifact ↗</a> }
                  @if (evidence.kind === 'logs') { <a [routerLink]="['/containers', data.incident.containerId, 'logs']" [queryParams]="{from:evidence.from,to:evidence.to}">Open synchronized logs →</a> }
                </article>
              }
            </div>
          </section>
        </section>

        <section class="terminal-panel analysis-panel">
          <div class="panel-title"><span>versioned · bounded · no external providers</span><strong>Local Analysis</strong></div>
          <div class="analysis-head">
            <div>
              <span class="chip">{{ data.analysis?.analyzerVersion || 'tracebag-local/1' }}</span>
              <span class="chip">{{ data.analysis?.status || 'not run' }}</span>
              @if (data.analysis?.envelope; as envelope) { <span class="chip">envelope v{{ envelope.schemaVersion }}</span><span class="chip">local only</span> }
            </div>
            <button type="button" [disabled]="isActive(data.incident.status) || analyzing()" (click)="runAnalysis(data.incident.id)">{{ analyzing() ? 'Analyzing…' : 'Run analysis again' }}</button>
          </div>
          @if (data.analysis?.errorMessage) { <p class="warning analysis-copy">{{ data.analysis?.errorMessage }}</p> }
          @if (data.analysis?.envelope; as envelope) {
            <div class="component-row">
              @for (component of envelope.components; track component.name + component.durationMilliseconds) {
                <span class="component"><strong>{{ component.name }}</strong> {{ component.status }} · {{ component.durationMilliseconds }}ms · {{ component.observationCount }} findings @if(component.error){<small>{{ component.error }}</small>}</span>
              }
            </div>
            <div class="analysis-grid">
              <div>
                <h3>Observations</h3>
                @for (observation of envelope.observations; track observation.id) {
                  <article class="finding observation"><div><span class="chip">{{ observation.analyzer }}</span><span class="chip">{{ observation.severity }}</span><span class="chip">{{ observation.confidence }} confidence</span></div><h3>{{ observation.title }}</h3><p>{{ observation.summary }}</p>@if(observation.data){<pre>{{ observation.data | json }}</pre>}<div class="refs">Raw evidence: @for(id of observation.evidenceIds; track id){<button type="button" (click)="selectEvidence(id)">{{ id }}</button>}</div></article>
                } @empty { <p class="empty">No deterministic observation crossed a reporting threshold.</p> }
              </div>
              <div class="analysis-side">
                <h3>Cross-signal correlations</h3>
                @for (correlation of envelope.correlations; track correlation.code) { <article><span class="chip">{{ correlation.confidence }} confidence</span><strong>{{ correlation.code }}</strong><p>{{ correlation.summary }}</p><div class="refs">@for(id of correlation.evidenceIds; track id){<button type="button" (click)="selectEvidence(id)">{{ id }}</button>}</div></article> } @empty { <p class="empty">A correlation needs matching evidence from at least two signals.</p> }
                <h3>Limitations</h3>
                @for (limitation of envelope.limitations; track limitation.code + limitation.evidenceId) { <article><strong>{{ limitation.code }}</strong><p>{{ limitation.summary }}</p>@if(limitation.evidenceId){<button type="button" (click)="selectEvidence(limitation.evidenceId)">Open evidence</button>}</article> }
              </div>
            </div>
          } @else { <p class="empty analysis-copy">Analysis runs automatically after capture. It can also be rerun without any external integration.</p> }
        </section>

        <section class="lower-grid">
          <section class="terminal-panel">
            <div class="panel-title"><span>local deterministic analysis</span><strong>Findings</strong></div>
            @for (finding of data.findings; track finding.id) {
              <article class="finding"><div><span class="chip">{{ finding.severity }}</span><span class="chip">{{ finding.confidence }} confidence</span></div><h3>{{ finding.title }}</h3><p>{{ finding.summary }}</p><div class="refs">Evidence: @for (id of finding.evidenceIds; track id) { <button type="button" (click)="selectEvidence(id)">{{ id }}</button> }</div></article>
            } @empty { <p class="empty">Findings appear after capture analysis.</p> }
          </section>
          <section class="terminal-panel export-panel">
            <div class="panel-title"><span>portable ZIP</span><strong>Create Tracebag</strong></div>
            <label class="check"><input type="checkbox" [(ngModel)]="includeLogs"> Include only the pinned, bounded log window</label>
            @for (evidence of artifactEvidence(data.evidence); track evidence.id) {
              <label class="check"><input type="checkbox" [checked]="selectedArtifacts().includes(evidence.artifactId!)" (change)="toggleArtifact(evidence.artifactId!)"> {{ evidence.title }} @if(evidence.sensitive){<strong> sensitive</strong>}</label>
            }
            @if (hasSelectedSensitive(data.evidence)) { <label class="check warning"><input type="checkbox" [(ngModel)]="confirmSensitive"> I explicitly approve sensitive artifact export</label> }
            <p class="export-note">Default export contains summaries, timeline, findings and checksums. It never silently adds historical logs or a full dump.</p>
            <button class="live" type="button" [disabled]="isActive(data.incident.status) || (hasSelectedSensitive(data.evidence) && !confirmSensitive)" (click)="export(data.incident.id)">Download .tracebag.zip</button>
            <label>Operator notes<textarea [(ngModel)]="notes" rows="4"></textarea></label><button type="button" (click)="saveNotes(data.incident.id)">Save notes</button>
            <div class="danger-zone"><strong>Delete incident</strong><p>This removes its timeline, evidence summaries, findings, and analysis. Linked captures remain until their normal retention policy removes them.</p><button class="danger" type="button" [disabled]="isActive(data.incident.status)" (click)="pendingDelete.set(true)">Delete incident</button></div>
          </section>
        </section>
      } @else { <section class="terminal-panel loading">{{ error() || 'Loading incident…' }}</section> }

      @if (pendingDelete()) {
        @if (detail(); as data) {
          <tb-confirm-dialog
            title="Delete incident?"
            [message]="'Permanently delete ' + data.incident.id + ' and its correlated timeline, evidence summaries, findings, and analyses. Linked recordings, jobs, and artifacts return to their normal retention policies.'"
            confirmLabel="Delete incident"
            (cancelled)="pendingDelete.set(false)"
            (confirmed)="deleteIncident(data.incident.id)" />
        }
      }
    </section>
  `,
  styles: [`
    .incident-detail{display:grid;gap:14px}.back-link{color:#7fffd4;text-decoration:none}.incident-head{display:grid;gap:14px;grid-template-columns:minmax(0,1fr) auto;padding:18px}.incident-head h2{font-size:1.35rem;margin:5px 0}.incident-head p{color:#8fa2b8}.head-status{display:grid;gap:6px;justify-items:end}.head-status small{color:#75899f}.progress{background:#111c25;grid-column:1/-1;height:5px}.progress span{background:#25d59b;display:block;height:100%;transition:width .3s}.warning{color:#ffb45d!important}.evidence-layout,.lower-grid{display:grid;gap:14px;grid-template-columns:minmax(280px,.8fr) minmax(0,1.2fr)}.timeline,.evidence-list{max-height:620px;overflow:auto}.timeline-event{background:transparent;border:0;border-bottom:1px solid #1d2a36;border-radius:0;display:grid;gap:10px;grid-template-columns:70px minmax(0,1fr);height:auto;padding:11px 14px;text-align:left;width:100%}.timeline-event.selected{background:#10251f}.timeline-event time{color:#75899f;font-family:monospace}.timeline-event span{display:grid;gap:3px}.timeline-event small{color:#8fa2b8}.timeline-event .warning{border-left:2px solid #ffb45d;padding-left:8px}.timeline-event .error{border-left:2px solid #ff647c;padding-left:8px}.evidence-card,.finding{border-bottom:1px solid #1d2a36;display:grid;gap:10px;padding:14px}.evidence-card.selected{background:#10251f;box-shadow:inset 3px 0 #25d59b}.evidence-card h3,.finding h3{font-size:1rem;margin:0}.evidence-card code{color:#75899f;font-size:.72rem;overflow-wrap:anywhere}.evidence-card pre,.observation pre{background:#080d12;color:#9fb1c7;font-size:.72rem;margin:0;max-height:180px;overflow:auto;padding:9px;white-space:pre-wrap}.evidence-card a{color:#7fffd4;font-size:.82rem}.chip.sensitive{border-color:#ff647c;color:#ff9ca8}.finding p,.export-note,.analysis-side p{color:#9fb1c7;line-height:1.5}.refs{color:#75899f;font-size:.78rem}.refs button{font-size:.7rem;margin:3px;padding:3px 6px;overflow-wrap:anywhere}.analysis-panel{padding-bottom:14px}.analysis-head{align-items:center;display:flex;justify-content:space-between;padding:14px}.analysis-head>div{display:flex;flex-wrap:wrap;gap:6px}.component-row{display:flex;flex-wrap:wrap;gap:8px;padding:0 14px 14px}.component{border:1px solid #263443;border-radius:6px;color:#9fb1c7;display:grid;gap:2px;padding:8px 10px}.component small{color:#ffb45d;max-width:360px}.analysis-grid{border-top:1px solid #1d2a36;display:grid;grid-template-columns:minmax(0,1.5fr) minmax(280px,.8fr)}.analysis-grid>div>h3{color:#8fa2b8;font-size:.75rem;letter-spacing:.08em;padding:12px 14px;text-transform:uppercase}.analysis-side{border-left:1px solid #1d2a36}.analysis-side article{border-bottom:1px solid #1d2a36;display:grid;gap:8px;padding:14px}.analysis-copy{margin:0;padding:0 14px 14px}.export-panel{display:grid;align-content:start;gap:12px;padding-bottom:16px}.export-panel>.panel-title{margin-bottom:2px}.export-panel>label,.export-panel>p,.export-panel>button{margin-left:14px;margin-right:14px}.check{align-items:center;display:flex;gap:8px;color:#9fb1c7}.check input{min-height:auto}.export-panel label:not(.check){color:#8fa2b8;display:grid;gap:6px}.export-panel textarea{background:#0b1219;border:1px solid #263443;border-radius:6px;color:#d8e2ef;padding:10px;resize:vertical}.danger-zone{border-top:1px solid #4f2730;display:grid;gap:7px;margin:8px 14px 0;padding-top:14px}.danger-zone p{color:#b9959e;font-size:.78rem;line-height:1.5;margin:0}.danger-zone button{justify-self:start}.loading,.empty{color:#75899f;padding:18px}@media(max-width:900px){.evidence-layout,.lower-grid,.analysis-grid{grid-template-columns:1fr}.analysis-side{border-left:0;border-top:1px solid #1d2a36}.incident-head{grid-template-columns:1fr}.head-status{justify-items:start}}@media(max-width:480px){.incident-head{padding:13px}.timeline-event{grid-template-columns:58px;padding:10px}.evidence-card,.finding{padding:11px}.analysis-head{align-items:stretch;flex-direction:column;gap:10px}}
  `]
})
export class IncidentDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute); private readonly router = inject(Router); private readonly api = inject(TracebagApiClient); private timer?: ReturnType<typeof setTimeout>;
  detail = signal<IncidentDetailDto | null>(null); error = signal(''); selectedEvidenceId = signal(''); selectedArtifacts = signal<string[]>([]);
  analyzing = signal(false);
  pendingDelete = signal(false);
  includeLogs = false; confirmSensitive = false; notes = '';
  async ngOnInit(): Promise<void> { await this.refresh(); }
  ngOnDestroy(): void { if (this.timer) clearTimeout(this.timer); }
  isActive(status: string): boolean { return ['queued', 'collecting', 'analyzing'].includes(status); }
  artifactEvidence(evidence: IncidentEvidenceDto[]): IncidentEvidenceDto[] { return evidence.filter((x) => !!x.artifactId); }
  hasSelectedSensitive(evidence: IncidentEvidenceDto[]): boolean { return evidence.some((x) => x.sensitive && x.artifactId && this.selectedArtifacts().includes(x.artifactId)); }
  selectEvidence(id: string | null): void { if (!id) return; this.selectedEvidenceId.set(id); setTimeout(() => document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'center' })); }
  toggleArtifact(id: string): void { this.selectedArtifacts.update((ids) => ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]); }
  export(id: string): void { window.location.assign(this.api.incidentExportUrl(id, this.includeLogs, this.selectedArtifacts(), this.confirmSensitive)); }
  async saveNotes(id: string): Promise<void> { try { await this.api.updateIncident(id, this.notes.trim() || null); await this.refresh(false); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not save notes.'); } }
  async runAnalysis(id: string): Promise<void> { this.analyzing.set(true); this.error.set(''); try { await this.api.analyzeIncident(id); await this.refresh(false); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not run local analysis.'); } finally { this.analyzing.set(false); } }
  async deleteIncident(id: string): Promise<void> { this.pendingDelete.set(false); this.error.set(''); try { await this.api.deleteIncident(id); await this.router.navigate(['/incidents']); } catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not delete incident.'); } }
  private async refresh(schedule = true): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id'); if (!id) return;
    try { const detail = await this.api.incident(id); this.detail.set(detail); this.notes = detail.incident.notes ?? ''; if (schedule && this.isActive(detail.incident.status)) this.timer = setTimeout(() => void this.refresh(), 1000); }
    catch (error) { this.error.set(error instanceof Error ? error.message : 'Could not load incident.'); }
  }
}
