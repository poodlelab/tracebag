import { inject, Injectable } from '@angular/core';
import {
  ArtifactDto,
  ContainerDto,
  ContainerOverviewDto,
  CounterRecordingDetailDto,
  CounterRecordingDto,
  CounterRecordingSamplesDto,
  DotnetProcessDto,
  DiagnosticJobDto,
  DiagnosticJobProfileDto,
  LogEventDto,
  LogSearchFilters,
  LogSearchResponseDto,
  GuidedIncidentProfileDto,
  IncidentDetailDto,
  AnalysisRunDto,
  IncidentSummaryDto,
  SystemStatusDto
} from '../tracebag.models';
import { TracebagStore } from '../tracebag.store';

@Injectable({ providedIn: 'root' })
export class TracebagApiClient {
  private readonly store = inject(TracebagStore);

  me(): Promise<{ authenticated: boolean; user: string | null }> {
    return this.request('/api/auth/me', {}, false);
  }

  csrf(): Promise<{ csrfToken: string }> {
    return this.request('/api/auth/csrf', {}, false);
  }

  login(userName: string, password: string): Promise<{ authenticated: boolean; user: string; csrfToken: string }> {
    return this.request('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ userName, password }),
      headers: { 'Content-Type': 'application/json' }
    }, false);
  }

  logout(): Promise<unknown> {
    return this.request('/api/auth/logout', { method: 'POST' });
  }

  containers(): Promise<ContainerDto[]> {
    return this.request('/api/containers');
  }

  containerOverview(containerId: string): Promise<ContainerOverviewDto> {
    return this.request(`/api/containers/${encodeURIComponent(containerId)}/overview`);
  }

  systemStatus(): Promise<SystemStatusDto> {
    return this.request('/api/system/status');
  }

  logs(containerId: string, tail: number, timestamps: boolean): Promise<LogEventDto[]> {
    return this.request(`/api/containers/${encodeURIComponent(containerId)}/logs?tail=${tail}&timestamps=${timestamps}`);
  }

  searchLogs(containerId: string, filters: LogSearchFilters): Promise<LogSearchResponseDto> {
    const params = new URLSearchParams();
    Object.entries(filters).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        params.set(key, String(value));
      }
    });
    return this.request(`/api/containers/${encodeURIComponent(containerId)}/logs/search?${params.toString()}`);
  }

  dotnetProcesses(containerId: string): Promise<DotnetProcessDto[]> {
    return this.request(`/api/containers/${containerId}/dotnet/processes`);
  }

  startCounters(containerId: string, processId: number, preset: string): Promise<{ sessionId: string; status: string }> {
    return this.request(`/api/containers/${containerId}/dotnet/counters`, {
      method: 'POST',
      body: JSON.stringify({ processId, preset }),
      headers: { 'Content-Type': 'application/json' }
    });
  }

  stopSession(sessionId: string): Promise<unknown> {
    return this.request(`/api/diagnostics/sessions/${sessionId}`, { method: 'DELETE' });
  }

  recordings(status?: string, containerId?: string): Promise<CounterRecordingDto[]> {
    const params = new URLSearchParams();
    if (status) {
      params.set('status', status);
    }

    if (containerId) {
      params.set('containerId', containerId);
    }

    const query = params.toString();
    return this.request(`/api/dotnet/recordings${query ? `?${query}` : ''}`);
  }

  startRecording(
    containerId: string,
    processId: number,
    preset: string,
    intervalSeconds: number,
    maxDurationMinutes: number,
    name: string
  ): Promise<{ id: string; status: string }> {
    return this.request(`/api/containers/${containerId}/dotnet/recordings`, {
      method: 'POST',
      body: JSON.stringify({
        processId,
        preset,
        intervalSeconds,
        maxDurationMinutes,
        name: name.trim() || null
      }),
      headers: { 'Content-Type': 'application/json' }
    });
  }

  recording(recordingId: string): Promise<CounterRecordingDetailDto> {
    return this.request(`/api/dotnet/recordings/${recordingId}`);
  }

  recordingSamples(recordingId: string, resolution = 'auto', from?: string, to?: string): Promise<CounterRecordingSamplesDto> {
    const params = new URLSearchParams({ resolution });
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    return this.request(`/api/dotnet/recordings/${recordingId}/samples?${params.toString()}`);
  }

  stopRecording(recordingId: string): Promise<CounterRecordingDto> {
    return this.request(`/api/dotnet/recordings/${recordingId}/stop`, { method: 'POST' });
  }

  updateRecording(recordingId: string, name: string, notes: string): Promise<CounterRecordingDto> {
    return this.request(`/api/dotnet/recordings/${recordingId}`, {
      method: 'PATCH',
      body: JSON.stringify({ name: name.trim() || null, notes: notes.trim() || null }),
      headers: { 'Content-Type': 'application/json' }
    });
  }

  recordingExportUrl(recordingId: string, format: 'csv' | 'json'): string {
    return `/api/dotnet/recordings/${encodeURIComponent(recordingId)}/export?format=${format}`;
  }

  deleteRecording(recordingId: string): Promise<unknown> {
    return this.request(`/api/dotnet/recordings/${recordingId}?confirm=${encodeURIComponent(recordingId)}`, { method: 'DELETE' });
  }

  diagnosticProfiles(): Promise<DiagnosticJobProfileDto[]> {
    return this.request('/api/diagnostic-jobs/profiles');
  }

  diagnosticJobs(containerId?: string): Promise<DiagnosticJobDto[]> {
    const query = containerId ? `?containerId=${encodeURIComponent(containerId)}` : '';
    return this.request(`/api/diagnostic-jobs${query}`);
  }

  diagnosticJob(jobId: string): Promise<DiagnosticJobDto> {
    return this.request(`/api/diagnostic-jobs/${encodeURIComponent(jobId)}`);
  }

  startDiagnosticJob(
    containerId: string,
    processId: number,
    profile: string,
    durationSeconds: number | null,
    confirmation: string | null
  ): Promise<DiagnosticJobDto> {
    return this.request(`/api/containers/${encodeURIComponent(containerId)}/diagnostic-jobs`, {
      method: 'POST',
      body: JSON.stringify({ processId, profile, durationSeconds, confirmation }),
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': crypto.randomUUID()
      }
    });
  }

  cancelDiagnosticJob(jobId: string): Promise<DiagnosticJobDto> {
    return this.request(`/api/diagnostic-jobs/${encodeURIComponent(jobId)}/cancel`, { method: 'POST' });
  }

  artifacts(): Promise<ArtifactDto[]> {
    return this.request('/api/artifacts');
  }

  deleteArtifact(artifactId: string): Promise<unknown> {
    return this.request(`/api/artifacts/${artifactId}`, { method: 'DELETE' });
  }

  incidentProfiles(): Promise<GuidedIncidentProfileDto[]> {
    return this.request('/api/incidents/profiles');
  }

  incidents(): Promise<IncidentSummaryDto[]> {
    return this.request('/api/incidents');
  }

  incident(id: string): Promise<IncidentDetailDto> {
    return this.request(`/api/incidents/${encodeURIComponent(id)}`);
  }

  createIncident(containerId: string, request: {
    processId: number;
    profile: string;
    title: string | null;
    reason: string | null;
    captureSeconds: number;
    includeTrace: boolean;
  }): Promise<IncidentSummaryDto> {
    return this.request(`/api/containers/${encodeURIComponent(containerId)}/incidents`, {
      method: 'POST',
      body: JSON.stringify(request),
      headers: { 'Content-Type': 'application/json' }
    });
  }

  updateIncident(id: string, notes: string | null, status: string | null = null): Promise<IncidentSummaryDto> {
    return this.request(`/api/incidents/${encodeURIComponent(id)}`, {
      method: 'PATCH',
      body: JSON.stringify({ notes, status }),
      headers: { 'Content-Type': 'application/json' }
    });
  }

  deleteIncident(id: string): Promise<unknown> {
    return this.request(`/api/incidents/${encodeURIComponent(id)}?confirm=${encodeURIComponent(id)}`, {
      method: 'DELETE'
    });
  }

  analyzeIncident(id: string): Promise<AnalysisRunDto> {
    return this.request(`/api/incidents/${encodeURIComponent(id)}/analysis`, { method: 'POST' });
  }

  incidentExportUrl(id: string, includePinnedLogs: boolean, artifactIds: string[], includeSensitiveArtifacts: boolean): string {
    const params = new URLSearchParams({ includePinnedLogs: String(includePinnedLogs) });
    artifactIds.forEach((artifactId) => params.append('artifactId', artifactId));
    if (includeSensitiveArtifacts) params.set('includeSensitiveArtifacts', 'true');
    return `/api/incidents/${encodeURIComponent(id)}/export?${params.toString()}`;
  }

  restart(containerId: string): Promise<unknown> {
    return this.request(`/api/containers/${containerId}/restart`, { method: 'POST' });
  }

  async request<T = unknown>(url: string, init: RequestInit = {}, includeCsrf = true): Promise<T> {
    const headers = new Headers(init.headers);
    if (includeCsrf && this.store.csrfToken()) {
      headers.set('X-CSRF-TOKEN', this.store.csrfToken());
    }

    const response = await fetch(url, {
      ...init,
      headers,
      credentials: 'same-origin'
    });

    if (!response.ok) {
      let message = `Request failed with status ${response.status}.`;
      try {
        const body = (await response.json()) as { message?: string };
        message = body.message || message;
      } catch {
        // Keep generic message.
      }

      throw new Error(message);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return (await response.json()) as T;
  }
}
