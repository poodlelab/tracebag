import { CounterMetricDto } from '../tracebag.models';

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`;
  }

  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export function formatCounterValue(metric: CounterMetricDto): string {
  if (metric.value === null) {
    return metric.valueText;
  }

  if (metric.name.includes('(%)')) {
    return `${metric.value.toLocaleString('en-US', { maximumFractionDigits: 2 })} %`;
  }

  if (metric.name.includes('(MB)')) {
    return `${metric.value.toLocaleString('en-US', { maximumFractionDigits: 1 })} MB`;
  }

  if (metric.name.includes('(B / 1 sec)')) {
    return `${formatBytes(metric.value)}/s`;
  }

  if (metric.name.endsWith('(B)')) {
    return formatBytes(metric.value);
  }

  return metric.value.toLocaleString('en-US', { maximumFractionDigits: 2 });
}

export function shortId(id: string): string {
  return id.slice(0, 12);
}

export function stateClass(value: string | undefined | null): string {
  const state = (value ?? '').toLowerCase();
  if (state.includes('running') || state.includes('healthy')) {
    return 'ok';
  }

  if (state.includes('restart') || state.includes('starting')) {
    return 'warn';
  }

  if (state.includes('exit') || state.includes('dead') || state.includes('unhealthy')) {
    return 'danger';
  }

  return 'neutral';
}
