import { CommonModule } from '@angular/common';
import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { ArtifactDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { formatBytes } from '../../shared/tracebag-format';
import { IconComponent } from '../../shared/icon.component';
import { ConfirmDialogComponent } from '../../shared/confirm-dialog.component';

@Component({
  selector: 'tb-artifacts-page',
  standalone: true,
  imports: [CommonModule, IconComponent, ConfirmDialogComponent],
  template: `
    <section class="page-header">
      <div>
        <span class="prompt">Captured evidence</span>
        <h2>{{ containerId ? 'Container Artifacts' : 'Artifacts' }}</h2>
        <p>Download and manage traces, dumps, and diagnostic snapshots produced by Tracebag.</p>
      </div>
      <button class="secondary" type="button" (click)="loadArtifacts()" [disabled]="store.loading()"><tb-icon name="refresh" /> Refresh</button>
    </section>

    <section class="artifact-list page-artifact-list">
      @for (artifact of visibleArtifacts(); track artifact.id) {
        <article class="content-panel artifact-item">
          <div>
            <strong>{{ artifact.type }}</strong>
            <small>{{ artifact.containerName }}</small>
            <small>{{ artifact.fileName }}</small>
            <small>{{ formatBytes(artifact.size) }} · {{ artifact.createdAt | date: 'short' }}</small>
            <small>state {{ artifact.state }} @if (artifact.sha256) { · sha256 {{ artifact.sha256.slice(0, 16) }}… }</small>
          </div>
          <div class="artifact-actions">
            <button type="button" [disabled]="artifact.state !== 'available'" (click)="downloadArtifact(artifact)"><tb-icon name="download" /> Download</button>
            @if (artifact.manifestFileName) {
              <a class="button-like" [href]="'/api/artifacts/' + artifact.id + '/manifest'">Manifest</a>
            }
            <button class="danger" type="button" (click)="pendingDelete.set(artifact)"><tb-icon name="trash" /> Delete</button>
          </div>
        </article>
      } @empty {
        <section class="content-panel">
          <div class="empty-state rich-empty"><tb-icon name="archive" /><strong>No artifacts yet</strong><span>Diagnostic captures and exported evidence will appear here.</span></div>
        </section>
      }
    </section>

    @if (pendingDelete(); as artifact) {
      <tb-confirm-dialog
        title="Delete artifact?"
        [message]="'This permanently removes ' + artifact.fileName + ' and its manifest from Tracebag.'"
        confirmLabel="Delete artifact"
        (cancelled)="pendingDelete.set(null)"
        (confirmed)="deleteArtifact(artifact)" />
    }

  `
})
export class ArtifactsPageComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly route = inject(ActivatedRoute);
  private routeSub?: Subscription;

  containerId = '';
  formatBytes = formatBytes;
  readonly pendingDelete = signal<ArtifactDto | null>(null);

  async ngOnInit(): Promise<void> {
    this.routeSub = (this.route.parent ?? this.route).paramMap.subscribe((params) => {
      this.containerId = params.get('id') ?? '';
    });

    await this.loadArtifacts();
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
  }

  visibleArtifacts(): ArtifactDto[] {
    if (!this.containerId) {
      return this.store.artifacts();
    }

    return this.store.artifacts().filter((artifact) => artifact.containerId === this.containerId);
  }

  async loadArtifacts(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setArtifacts(await this.api.artifacts());
    });
  }

  downloadArtifact(artifact: ArtifactDto): void {
    window.location.href = `/api/artifacts/${artifact.id}/download`;
  }

  async deleteArtifact(artifact: ArtifactDto): Promise<void> {
    this.pendingDelete.set(null);
    await this.withLoading(async () => {
      await this.api.deleteArtifact(artifact.id);
      this.store.setArtifacts(await this.api.artifacts());
    });
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try {
      await work();
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Unable to load artifacts.');
    } finally {
      this.store.setLoading(false);
    }
  }
}
