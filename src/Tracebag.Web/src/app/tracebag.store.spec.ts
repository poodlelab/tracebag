import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { TracebagStore } from './tracebag.store';

describe('TracebagStore', () => {
  let store: InstanceType<typeof TracebagStore>;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TracebagStore] });
    store = TestBed.inject(TracebagStore);
  });

  it('keeps authentication and CSRF state together', () => {
    store.setAuthenticated(true, 'operator', 'csrf-one');
    expect(store.authenticated()).toBe(true);
    expect(store.user()).toBe('operator');
    expect(store.csrfToken()).toBe('csrf-one');

    store.setAuthenticated(false, '', '');
    expect(store.authenticated()).toBe(false);
    expect(store.csrfToken()).toBe('');
  });

  it('clears target-specific live state when the selected container changes', () => {
    store.setContainers([
      { id: 'one', displayName: 'One' },
      { id: 'two', displayName: 'Two' }
    ] as never);
    store.setLogs([{ line: 'old' }] as never);
    store.setProcesses([{ pid: 42 }] as never);
    store.setSelectedProcessId(42);
    store.setCounterSession('session-one');

    store.selectContainer('two');

    expect(store.selectedContainerId()).toBe('two');
    expect(store.logs()).toEqual([]);
    expect(store.processes()).toEqual([]);
    expect(store.selectedProcessId()).toBeNull();
    expect(store.counterSessionId()).toBe('');
  });

  it('does not append live logs while paused and bounds resumed history', () => {
    store.setLogPaused(true);
    store.appendLog({ line: 'ignored' } as never);
    expect(store.logs()).toEqual([]);

    store.setLogPaused(false);
    for (let index = 0; index < 3_010; index += 1) {
      store.appendLog({ line: String(index) } as never);
    }
    expect(store.logs()).toHaveLength(3_000);
    expect((store.logs()[0] as { line: string }).line).toBe('10');
  });
});
