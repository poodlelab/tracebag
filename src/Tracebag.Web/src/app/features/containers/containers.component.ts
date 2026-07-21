import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { ContainerDto } from '../../tracebag.models';
import { TracebagStore } from '../../tracebag.store';
import { shortId, stateClass } from '../../shared/tracebag-format';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'tb-containers',
  standalone: true,
  imports: [CommonModule, RouterLink, IconComponent],
  template: `
    <section class="page-header">
      <div>
        <span class="prompt">Environment</span>
        <h2>Containers</h2>
        <p>Inspect health, logs, runtime metrics, and diagnostic captures across your services.</p>
      </div>
      <button class="secondary" type="button" (click)="loadContainers()" [disabled]="store.loading()"><tb-icon name="refresh" /> Refresh</button>
    </section>

    <section class="container-grid">
      @for (container of store.containers(); track container.id) {
        <article class="content-panel container-card">
          <div class="selected-header">
            <span class="status-dot" [class.ok]="stateClass(container.status) === 'ok'" [class.warn]="stateClass(container.status) === 'warn'" [class.danger]="stateClass(container.status) === 'danger'"></span>
            <div>
              <strong>{{ container.displayName }}</strong>
              <small>{{ container.serviceName || container.name }} · {{ container.kind }}</small>
            </div>
          </div>

          <dl class="kv-list compact-kv">
            <div>
              <dt>Status</dt>
              <dd>{{ container.status }}</dd>
            </div>
            <div>
              <dt>Image</dt>
              <dd>{{ container.image }}</dd>
            </div>
            <div>
              <dt>ID</dt>
              <dd>{{ shortId(container.id) }}</dd>
            </div>
          </dl>

          <div class="button-row">
            <a class="button-like primary" [routerLink]="['/containers', container.id, 'overview']">Inspect <tb-icon name="arrow-right" /></a>
            <a class="button-like" [routerLink]="['/containers', container.id, 'logs']"><tb-icon name="list" /> Logs</a>
            @if (container.kind === 'dotnet') {
              <a class="button-like" [routerLink]="['/containers', container.id, 'metrics']"><tb-icon name="metrics" /> Metrics</a>
            }
          </div>
        </article>
      } @empty {
        <section class="content-panel">
          <div class="empty-state rich-empty"><tb-icon name="container" /><strong>No containers discovered</strong><span>Add the <code>tracebag.enabled=true</code> label to a container, then refresh this page.</span></div>
        </section>
      }
    </section>
  `
})
export class ContainersComponent implements OnInit {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);

  stateClass = stateClass;
  shortId = shortId;

  async ngOnInit(): Promise<void> {
    if (!this.store.containers().length) {
      await this.loadContainers();
    }
  }

  async loadContainers(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setContainers(await this.api.containers());
    });
  }

  private async withLoading(work: () => Promise<void>): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try {
      await work();
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Unable to refresh containers.');
    } finally {
      this.store.setLoading(false);
    }
  }
}
