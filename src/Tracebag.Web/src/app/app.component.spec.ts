import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AppComponent } from './app.component';
import { TracebagApiClient } from './core/tracebag-api.client';
import { TracebagStore } from './tracebag.store';

@Component({ standalone: true, template: '' })
class EmptyRouteComponent {}

describe('AppComponent authentication bootstrap', () => {
  const api = {
    me: vi.fn(),
    csrf: vi.fn(),
    containers: vi.fn(),
    artifacts: vi.fn(),
    recordings: vi.fn(),
    logout: vi.fn()
  };

  beforeEach(() => {
    Object.values(api).forEach(mock => mock.mockReset());
    TestBed.configureTestingModule({
      providers: [
        TracebagStore,
        provideRouter([
          { path: 'login', component: EmptyRouteComponent },
          { path: 'containers', component: EmptyRouteComponent }
        ]),
        { provide: TracebagApiClient, useValue: api }
      ]
    });
  });

  it('restores the session, fetches CSRF, and loads initial operator state', async () => {
    api.me.mockResolvedValue({ authenticated: true, user: 'admin' });
    api.csrf.mockResolvedValue({ csrfToken: 'csrf-token' });
    api.containers.mockResolvedValue([{ id: 'container-one', displayName: 'Demo' }]);
    api.artifacts.mockResolvedValue([{ id: 'artifact-one' }]);
    api.recordings.mockResolvedValue([{ id: 'recording-one' }]);
    const component = TestBed.runInInjectionContext(() => new AppComponent());

    await component.ngOnInit();

    const store = TestBed.inject(TracebagStore);
    expect(component.authReady()).toBe(true);
    expect(store.authenticated()).toBe(true);
    expect(store.csrfToken()).toBe('csrf-token');
    expect(store.containers()).toHaveLength(1);
    expect(store.artifacts()).toHaveLength(1);
    expect(store.recordings()).toHaveLength(1);
    expect(TestBed.inject(Router).url).toBe('/containers');
  });

  it('finishes bootstrap and routes to login when session discovery fails', async () => {
    api.me.mockRejectedValue(new Error('offline'));
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/containers');
    const component = TestBed.runInInjectionContext(() => new AppComponent());

    await component.ngOnInit();

    expect(component.authReady()).toBe(true);
    expect(TestBed.inject(TracebagStore).authenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });
});
