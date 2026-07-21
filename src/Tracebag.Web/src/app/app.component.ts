import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { TracebagApiClient } from './core/tracebag-api.client';
import { TracebagStore } from './tracebag.store';
import { IconComponent } from './shared/icon.component';
import { BrandMarkComponent } from './shared/brand-mark.component';
import { Subscription } from 'rxjs';

@Component({
  selector: 'tb-root',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, IconComponent, BrandMarkComponent],
  templateUrl: './app.component.html'
})
export class AppComponent implements OnInit, OnDestroy {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly router = inject(Router);

  authReady = signal(false);
  currentUrl = signal(this.router.url);
  readonly currentSection = computed(() => {
    const segment = this.currentUrl().split('?')[0].split('/').filter(Boolean)[0] ?? 'containers';
    return segment.charAt(0).toUpperCase() + segment.slice(1);
  });
  private readonly routerSubscription: Subscription;

  constructor() {
    this.routerSubscription = this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe((event) => {
        this.currentUrl.set(event.urlAfterRedirects);
      });
  }

  ngOnDestroy(): void {
    this.routerSubscription.unsubscribe();
  }

  dismissError(): void {
    this.store.setError('');
  }

  async ngOnInit(): Promise<void> {
    await this.refreshAuth();
  }

  async loadContainers(): Promise<void> {
    await this.withLoading(async () => {
      this.store.setContainers(await this.api.containers());
    });
  }

  async logout(): Promise<void> {
    try {
      await this.api.logout();
    } finally {
      this.store.setAuthenticated(false, '', '');
      await this.router.navigateByUrl('/login');
    }
  }

  private async refreshAuth(): Promise<void> {
    try {
      const response = await this.api.me();
      if (response.authenticated) {
        const csrf = await this.api.csrf();
        this.store.setAuthenticated(true, response.user ?? '', csrf.csrfToken);
        await this.loadInitialData();
        if (this.currentUrl() === '/' || this.currentUrl() === '/login') {
          await this.router.navigateByUrl('/containers');
        }
      } else if (!this.currentUrl().startsWith('/login')) {
        await this.router.navigateByUrl('/login');
      }
    } catch {
      if (!this.currentUrl().startsWith('/login')) {
        await this.router.navigateByUrl('/login');
      }
    } finally {
      this.authReady.set(true);
    }
  }

  private async loadInitialData(): Promise<void> {
    await this.withLoading(async () => {
      const [containers, artifacts] = await Promise.all([
        this.api.containers(),
        this.api.artifacts()
      ]);
      this.store.setContainers(containers);
      this.store.setArtifacts(artifacts);

      try {
        this.store.setRecordings(await this.api.recordings());
      } catch {
        this.store.setRecordings([]);
      }
    });
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
