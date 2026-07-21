import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import {
  ArtifactDto,
  ContainerDto,
  CounterMetricDto,
  CounterRecordingDetailDto,
  CounterRecordingDto,
  CounterRecordingSamplesDto,
  DotnetProcessDto,
  DiagnosticJobDto,
  LogEventDto
} from './tracebag.models';

interface TracebagState {
  authenticated: boolean;
  user: string;
  csrfToken: string;
  loading: boolean;
  error: string;
  containers: ContainerDto[];
  selectedContainerId: string;
  logs: LogEventDto[];
  logFilter: string;
  logPaused: boolean;
  processes: DotnetProcessDto[];
  selectedProcessId: number | null;
  counterOutput: LogEventDto[];
  counterMetrics: CounterMetricDto[];
  counterLastUpdatedAt: string;
  counterSessionId: string;
  recordings: CounterRecordingDto[];
  recordingDetail: CounterRecordingDetailDto | null;
  recordingSamples: CounterRecordingSamplesDto | null;
  artifacts: ArtifactDto[];
  diagnosticJobs: DiagnosticJobDto[];
}

const initialState: TracebagState = {
  authenticated: false,
  user: '',
  csrfToken: '',
  loading: false,
  error: '',
  containers: [],
  selectedContainerId: '',
  logs: [],
  logFilter: '',
  logPaused: false,
  processes: [],
  selectedProcessId: null,
  counterOutput: [],
  counterMetrics: [],
  counterLastUpdatedAt: '',
  counterSessionId: '',
  recordings: [],
  recordingDetail: null,
  recordingSamples: null,
  artifacts: [],
  diagnosticJobs: []
};

export const TracebagStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withMethods((store) => ({
    setAuthenticated(authenticated: boolean, user: string, csrfToken = ''): void {
      patchState(store, { authenticated, user, csrfToken });
    },
    setCsrfToken(csrfToken: string): void {
      patchState(store, { csrfToken });
    },
    setLoading(loading: boolean): void {
      patchState(store, { loading });
    },
    setError(error: string): void {
      patchState(store, { error });
    },
    setContainers(containers: ContainerDto[]): void {
      patchState(store, {
        containers,
        selectedContainerId: store.selectedContainerId() || containers[0]?.id || ''
      });
    },
    selectContainer(selectedContainerId: string): void {
      if (store.selectedContainerId() === selectedContainerId) {
        return;
      }

      patchState(store, {
        selectedContainerId,
        logs: [],
        processes: [],
        selectedProcessId: null,
        counterOutput: [],
        counterMetrics: [],
        counterLastUpdatedAt: '',
        counterSessionId: '',
        recordingDetail: null,
        recordingSamples: null
      });
    },
    setLogs(logs: LogEventDto[]): void {
      patchState(store, { logs });
    },
    appendLog(log: LogEventDto): void {
      if (!store.logPaused()) {
        patchState(store, { logs: [...store.logs(), log].slice(-3000) });
      }
    },
    setLogFilter(logFilter: string): void {
      patchState(store, { logFilter });
    },
    setLogPaused(logPaused: boolean): void {
      patchState(store, { logPaused });
    },
    clearLogs(): void {
      patchState(store, { logs: [] });
    },
    setProcesses(processes: DotnetProcessDto[]): void {
      patchState(store, {
        processes,
        selectedProcessId: processes.length === 1 ? processes[0].pid : store.selectedProcessId()
      });
    },
    setSelectedProcessId(selectedProcessId: number | null): void {
      patchState(store, { selectedProcessId });
    },
    setCounterSession(counterSessionId: string): void {
      patchState(store, { counterSessionId, counterOutput: [], counterMetrics: [], counterLastUpdatedAt: '' });
    },
    appendCounterOutput(log: LogEventDto): void {
      const counterOutput = [...store.counterOutput(), log].slice(-200);
      patchState(store, { counterOutput });
    },
    upsertCounterMetric(metric: CounterMetricDto): void {
      const metrics = [...store.counterMetrics()];
      const existingIndex = metrics.findIndex((entry) => entry.id === metric.id);
      if (existingIndex >= 0) {
        metrics[existingIndex] = metric;
      } else {
        metrics.push(metric);
      }

      patchState(store, {
        counterMetrics: metrics,
        counterLastUpdatedAt: metric.receivedAt
      });
    },
    stopCounterSession(): void {
      patchState(store, { counterSessionId: '' });
    },
    setRecordings(recordings: CounterRecordingDto[]): void {
      patchState(store, { recordings });
    },
    upsertRecording(recording: CounterRecordingDto): void {
      const recordings = [...store.recordings()];
      const index = recordings.findIndex((entry) => entry.id === recording.id);
      if (index >= 0) {
        recordings[index] = recording;
      } else {
        recordings.unshift(recording);
      }

      patchState(store, { recordings });
    },
    setRecordingDetail(recordingDetail: CounterRecordingDetailDto | null): void {
      patchState(store, { recordingDetail });
    },
    setRecordingSamples(recordingSamples: CounterRecordingSamplesDto | null): void {
      patchState(store, { recordingSamples });
    },
    setArtifacts(artifacts: ArtifactDto[]): void {
      patchState(store, { artifacts });
    },
    setDiagnosticJobs(diagnosticJobs: DiagnosticJobDto[]): void {
      patchState(store, { diagnosticJobs });
    },
    upsertDiagnosticJob(job: DiagnosticJobDto): void {
      const jobs = [...store.diagnosticJobs()];
      const index = jobs.findIndex((item) => item.id === job.id);
      if (index >= 0) jobs[index] = job;
      else jobs.unshift(job);
      patchState(store, { diagnosticJobs: jobs });
    }
  }))
);
