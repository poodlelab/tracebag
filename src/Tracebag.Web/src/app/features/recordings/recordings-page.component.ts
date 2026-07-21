import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { CounterRecordingDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'tb-recordings-page',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  template: `
    <section class="page-header">
      <div>
        <span class="prompt">Runtime history</span>
        <h2>Counter recordings</h2>
        <p>Review saved .NET runtime sessions and compare how your services behaved over time.</p>
      </div>
      <button class="secondary" type="button" (click)="load()" [disabled]="store.loading()"><tb-icon name="refresh" /> Refresh</button>
    </section>

    <section class="content-panel route-panel">
      <div class="panel-title list-heading">
        <div>
          <span>All recordings</span>
          <small>{{ store.recordings().length }} {{ store.recordings().length === 1 ? 'session' : 'sessions' }}</small>
        </div>
      </div>

      <div class="recording-list">
        @for (recording of store.recordings(); track recording.id) {
          <article class="recording-row">
            <div>
              <strong>{{ recording.name || recording.containerName }}</strong>
              <span>{{ recording.containerName }} · {{ recording.status }} · {{ recording.sampleCount }} samples</span>
            </div>
            <span>{{ recording.startedAt | date: 'short' }}</span>
            <div class="row-actions">
              <a class="button-like" [routerLink]="['/recordings', recording.id]">View <tb-icon name="arrow-right" /></a>
              @if (isActive(recording)) {
                <button type="button" (click)="stop(recording)">Stop</button>
              }
            </div>
          </article>
        } @empty {
          <div class="empty-state rich-empty"><tb-icon name="recording" /><strong>No recordings yet</strong><span>Start a recording from a .NET container's Metrics tab.</span></div>
        }
      </div>
    </section>
  `
})
export class RecordingsPageComponent implements OnInit {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setRecordings(await this.api.recordings());
    });
  }

  async stop(recording: CounterRecordingDto): Promise<void> {
    await this.withLoading(async () => {
      const stopped = await this.api.stopRecording(recording.id);
      this.store.upsertRecording(stopped);
    });
  }

  isActive(recording: CounterRecordingDto): boolean {
    return recording.status === 'starting' || recording.status === 'running' || recording.status === 'stopping';
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try {
      await work();
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Unable to load recordings.');
    } finally {
      this.store.setLoading(false);
    }
  }
}
