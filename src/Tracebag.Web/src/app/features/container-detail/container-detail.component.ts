import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Subscription } from 'rxjs';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { TracebagStore } from '../../tracebag.store';
import { shortId, stateClass } from '../../shared/tracebag-format';
import { IconComponent } from '../../shared/icon.component';

@Component({
  selector: 'tb-container-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, IconComponent],
  template: `
    <section class="container-detail-shell">
      <header class="container-header content-panel">
        <div class="selected-header">
          <span class="status-dot" [class.ok]="stateClass(selectedContainer()?.status) === 'ok'" [class.warn]="stateClass(selectedContainer()?.status) === 'warn'" [class.danger]="stateClass(selectedContainer()?.status) === 'danger'"></span>
          <div>
            <strong>{{ selectedContainer()?.displayName || 'Container' }}</strong>
            <small>{{ selectedContainer()?.serviceName || selectedContainer()?.name }}</small>
          </div>
        </div>

        @if (selectedContainer()) {
          <div class="header-meta">
            <span>{{ selectedContainer()?.kind }}</span>
            <span>{{ selectedContainer()?.state }}</span>
            <span>{{ shortId(selectedContainer()!.id) }}</span>
          </div>
        }
      </header>

      <nav class="container-tabs content-panel" aria-label="Container navigation">
        <a routerLink="overview" routerLinkActive="active"><tb-icon name="gauge" /> Overview</a>
        <a routerLink="logs" routerLinkActive="active"><tb-icon name="list" /> Logs</a>
        <a routerLink="metrics" routerLinkActive="active"><tb-icon name="metrics" /> Metrics</a>
        <a routerLink="diagnostics" routerLinkActive="active"><tb-icon name="activity" /> Diagnostics</a>
        <a routerLink="artifacts" routerLinkActive="active"><tb-icon name="archive" /> Artifacts</a>
      </nav>

      <router-outlet></router-outlet>
    </section>
  `
})
export class ContainerDetailComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private routeSub?: Subscription;

  readonly selectedContainer = computed(() =>
    this.store.containers().find((container) => container.id === this.store.selectedContainerId()) ?? null
  );

  stateClass = stateClass;
  shortId = shortId;

  async ngOnInit(): Promise<void> {
    if (!this.store.containers().length) {
      await this.loadContainers();
    }

    this.routeSub = this.route.paramMap.subscribe((params) => {
      const containerId = params.get('id') ?? '';
      if (!containerId) {
        return;
      }

      if (this.store.selectedContainerId() && this.store.selectedContainerId() !== containerId && this.store.counterSessionId()) {
        const sessionId = this.store.counterSessionId();
        this.store.stopCounterSession();
        void this.api.stopSession(sessionId).catch(() => {
          this.store.setError('The previous counter session could not be stopped cleanly.');
        });
      }

      this.store.selectContainer(containerId);
      if (this.store.containers().length && !this.selectedContainer()) {
        void this.router.navigateByUrl('/containers');
      }
    });
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
  }

  private async loadContainers(): Promise<void> {
    this.store.setLoading(true);
    this.store.setError('');
    try {
      this.store.setContainers(await this.api.containers());
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Unable to load this container.');
    } finally {
      this.store.setLoading(false);
    }
  }
}
