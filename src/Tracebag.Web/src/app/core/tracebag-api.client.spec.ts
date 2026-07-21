import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { TracebagStore } from '../tracebag.store';
import { TracebagApiClient } from './tracebag-api.client';

describe('TracebagApiClient', () => {
  let api: TracebagApiClient;
  let store: InstanceType<typeof TracebagStore>;
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TracebagApiClient, TracebagStore] });
    api = TestBed.inject(TracebagApiClient);
    store = TestBed.inject(TracebagStore);
    fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);
  });

  it('adds the current CSRF token and same-origin credentials to mutations', async () => {
    store.setAuthenticated(true, 'admin', 'csrf-token');
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ status: 'ok' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' }
    }));

    await api.restart('container/one');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/containers/container/one/restart');
    expect(init.credentials).toBe('same-origin');
    expect(new Headers(init.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
  });

  it('does not send a stale CSRF token while logging in', async () => {
    store.setCsrfToken('stale-token');
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      authenticated: true,
      user: 'admin',
      csrfToken: 'fresh-token'
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    await api.login('admin', 'secret');

    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect(new Headers(init.headers).has('X-CSRF-TOKEN')).toBe(false);
  });

  it('surfaces the API message and falls back safely for non-JSON errors', async () => {
    fetchMock
      .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'Container is no longer available.' }), {
        status: 409,
        headers: { 'Content-Type': 'application/json' }
      }))
      .mockResolvedValueOnce(new Response('gateway failure', { status: 502 }));

    await expect(api.containers()).rejects.toThrow('Container is no longer available.');
    await expect(api.containers()).rejects.toThrow('Request failed with status 502.');
  });

  it('requires the durable identifier in destructive delete requests', async () => {
    store.setCsrfToken('csrf-token');
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    await api.deleteIncident('incident with spaces');

    expect(fetchMock.mock.calls[0][0]).toBe(
      '/api/incidents/incident%20with%20spaces?confirm=incident%20with%20spaces');
  });
});
