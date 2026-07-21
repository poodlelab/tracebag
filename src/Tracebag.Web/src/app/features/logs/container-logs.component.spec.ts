import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TracebagApiClient } from '../../core/tracebag-api.client';
import { TracebagStore } from '../../tracebag.store';
import { ContainerLogsComponent } from './container-logs.component';

class FakeEventSource {
  static instances: FakeEventSource[] = [];
  readonly listeners = new Map<string, EventListener>();
  onerror: ((event: Event) => void) | null = null;
  closed = false;

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this);
  }

  addEventListener(name: string, listener: EventListener): void {
    this.listeners.set(name, listener);
  }

  close(): void {
    this.closed = true;
  }
}

describe('ContainerLogsComponent SSE lifecycle', () => {
  beforeEach(() => {
    FakeEventSource.instances = [];
    vi.stubGlobal('EventSource', FakeEventSource);
    TestBed.configureTestingModule({
      providers: [
        TracebagStore,
        { provide: TracebagApiClient, useValue: { searchLogs: vi.fn() } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => null } } } }
      ]
    });
  });

  it('shows native EventSource reconnect state and closes the stream explicitly', () => {
    const store = TestBed.inject(TracebagStore);
    store.setContainers([{ id: 'container-one', displayName: 'Demo' }] as never);
    const component = TestBed.runInInjectionContext(() => new ContainerLogsComponent());

    component.toggleLive();
    const source = FakeEventSource.instances[0];
    expect(source.url).toContain('/api/containers/container-one/logs/live');
    expect(component.liveState()).toBe('connecting');

    source.listeners.get('open')?.(new Event('open'));
    expect(component.liveState()).toBe('live');
    source.onerror?.(new Event('error'));
    expect(component.liveState()).toBe('reconnecting');

    component.toggleLive();
    expect(source.closed).toBe(true);
    expect(component.liveState()).toBe('stopped');
  });
});
