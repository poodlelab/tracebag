import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { TracebagStore } from '../../tracebag.store';
import { IconComponent } from '../../shared/icon.component';
import { BrandMarkComponent } from '../../shared/brand-mark.component';

@Component({
  selector: 'tb-login',
  standalone: true,
  imports: [FormsModule, IconComponent, BrandMarkComponent],
  template: `
    <main class="login-shell">
      <form class="login-panel" (ngSubmit)="login()">
        <div class="login-brand">
          <tb-brand-mark class="large" />
          <span>Tracebag</span>
        </div>
        <div class="login-copy">
          <h1>Welcome back</h1>
          <p>Sign in to inspect your container environment.</p>
        </div>
        <label>
          Username
          <input name="user" [(ngModel)]="loginUser" autocomplete="username" required>
        </label>
        <label>
          Password
          <input name="password" [(ngModel)]="loginPassword" type="password" autocomplete="current-password" required>
        </label>
        <button class="primary login-submit" type="submit" [disabled]="store.loading()">
          @if (store.loading()) { <span class="spinner small"></span> Signing in… } @else { Sign in <tb-icon name="arrow-right" /> }
        </button>
        @if (store.error()) {
          <p class="login-error" role="alert"><tb-icon name="warning" /> {{ store.error() }}</p>
        }
        <small class="login-footnote">Your credentials stay inside this Tracebag installation.</small>
      </form>
    </main>
  `
})
export class LoginComponent {
  readonly store = inject(TracebagStore);
  private readonly api = inject(TracebagApiClient);
  private readonly router = inject(Router);

  loginUser = 'admin';
  loginPassword = '';

  async login(): Promise<void> {
    this.store.setError('');
    try {
      const response = await this.api.login(this.loginUser, this.loginPassword);
      this.store.setAuthenticated(response.authenticated, response.user, response.csrfToken);
      this.loginPassword = '';
      await this.loadInitialData();
      await this.router.navigateByUrl('/containers');
    } catch (error) {
      this.store.setError(error instanceof Error ? error.message : 'Unable to sign in. Please try again.');
    }
  }

  private async loadInitialData(): Promise<void> {
    this.store.setLoading(true);
    try {
      const [containers, artifacts] = await Promise.all([
        this.api.containers(),
        this.api.artifacts()
      ]);
      this.store.setContainers(containers);
      this.store.setArtifacts(artifacts);
    } finally {
      this.store.setLoading(false);
    }
  }
}
