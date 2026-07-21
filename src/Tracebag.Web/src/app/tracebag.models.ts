export interface ContainerDto {
  id: string;
  dockerId: string;
  identitySource: string;
  name: string;
  serviceName: string;
  projectName: string | null;
  image: string;
  status: string;
  state: string;
  kind: string;
  displayName: string;
  created: string;
  restartAllowed: boolean;
}

export interface ContainerOverviewDto {
  container: ContainerDto;
  inspect: ContainerInspectDto;
  resources: ContainerResourceStatsDto;
  recentEvents: DockerEventDto[];
  knownInstanceCount: number;
}

export interface ContainerInspectDto {
  platform: string;
  driver: string;
  running: boolean;
  paused: boolean;
  restarting: boolean;
  dead: boolean;
  oomKilled: boolean;
  pid: number;
  exitCode: number;
  restartCount: number;
  startedAt: string | null;
  finishedAt: string | null;
  health: ContainerHealthDto;
}

export interface ContainerHealthDto {
  status: string;
  failingStreak: number;
  recentChecks: ContainerHealthLogDto[];
}

export interface ContainerHealthLogDto {
  startedAt: string;
  endedAt: string;
  exitCode: number;
  output: string;
}

export interface ContainerResourceStatsDto {
  available: boolean;
  unavailableReason: string | null;
  readAt: string | null;
  cpuPercent: number | null;
  memoryUsageBytes: number | null;
  memoryLimitBytes: number | null;
  memoryPercent: number | null;
  networkRxBytes: number | null;
  networkTxBytes: number | null;
  blockReadBytes: number | null;
  blockWriteBytes: number | null;
  pids: number | null;
}

export interface DockerEventDto {
  id: number | null;
  containerId: string;
  dockerId: string;
  action: string;
  timestamp: string;
  attributes: Record<string, string>;
}

export interface SystemStatusDto {
  version: string;
  startedAt: string;
  uptime: string;
  docker: SystemDependencyDto;
  database: SystemDependencyDto;
  artifactStorage: SystemDependencyDto;
  runnerImage: SystemDependencyDto;
  dataRetention: SystemDependencyDto;
  eventCollector: EventCollectorStatusDto;
  logIngestion: LogIngestionStatusDto;
  activeTargetCount: number;
  knownTargetCount: number;
}

export interface LogIngestionStatusDto {
  status: string;
  activeCollectors: number;
  queueDepth: number;
  queueCapacity: number;
  persistedEntries: number;
  droppedEntries: number;
  duplicateEntries: number;
  retentionDeletedEntries: number;
  storedEntries: number;
  storedBytes: number;
  lastPersistedAt: string | null;
  newestLogTimestamp: string | null;
  ingestionLagSeconds: number | null;
  lastError: string | null;
}

export interface SystemDependencyDto {
  status: string;
  message: string;
  details: Record<string, unknown>;
}

export interface EventCollectorStatusDto {
  status: string;
  lastConnectedAt: string | null;
  lastEventAt: string | null;
  retainedEventCount: number;
  lastError: string | null;
}

export interface LogEventDto {
  stream: 'stdout' | 'stderr';
  line: string;
  timestamp: string | null;
}

export interface LogSearchFilters {
  text?: string;
  level?: string;
  stream?: string;
  exceptionOnly?: boolean;
  traceId?: string;
  from?: string;
  to?: string;
  cursor?: string;
  limit?: number;
}

export interface LogSearchResponseDto {
  items: LogSearchEntryDto[];
  nextCursor: string | null;
  hasMore: boolean;
}

export interface LogSearchEntryDto {
  id: number;
  containerId: string;
  containerName: string;
  dockerId: string;
  timestamp: string;
  receivedAt: string;
  stream: 'stdout' | 'stderr';
  rawLine: string;
  message: string;
  level: string | null;
  exceptionType: string | null;
  traceId: string | null;
  properties: Record<string, unknown>;
}

export interface CounterMetricDto {
  id: string;
  timestamp: string;
  provider: string;
  name: string;
  counterType: string;
  value: number | null;
  valueText: string;
  receivedAt: string;
}

export interface CounterRecordingDto {
  id: string;
  containerId: string;
  containerName: string;
  processId: number;
  preset: string;
  intervalSeconds: number;
  startedAt: string;
  stoppedAt: string | null;
  lastSampleAt: string | null;
  status: string;
  sampleCount: number;
  createdBy: string;
  name: string | null;
  stopReason: string | null;
  errorMessage: string | null;
  notes: string | null;
  runtimeMajor: number;
  runnerImage: string;
  toolVersion: string;
}

export interface CounterSeriesDescriptorDto {
  provider: string;
  name: string;
  counterType: string;
  sampleCount: number;
  firstSampleAt: string | null;
  lastSampleAt: string | null;
}

export interface CounterRecordingDetailDto {
  recording: CounterRecordingDto;
  series: CounterSeriesDescriptorDto[];
}

export interface CounterRecordingSamplesDto {
  recordingId: string;
  resolution: string;
  availableResolutions: string[];
  truncated: boolean;
  series: CounterSeriesDto[];
}

export interface CounterSeriesDto {
  provider: string;
  name: string;
  counterType: string;
  summary: CounterSeriesSummaryDto;
  points: CounterSamplePointDto[];
}

export interface CounterSeriesSummaryDto {
  minimum: number;
  maximum: number;
  average: number;
  peakAt: string;
  sampleCount: number;
}

export interface CounterSamplePointDto {
  timestamp: string;
  value: number;
  minimum: number;
  maximum: number;
  count: number;
}

export interface DotnetProcessDto {
  pid: number;
  name: string;
  commandLine: string;
}

export interface ArtifactDto {
  id: string;
  containerId: string;
  containerName: string;
  type: string;
  fileName: string;
  createdAt: string;
  size: number;
  createdBy: string;
  expiresAt: string;
  diagnosticJobId: string | null;
  sha256: string | null;
  manifestFileName: string | null;
  state: string;
}

export interface DiagnosticJobProfileDto {
  id: string;
  displayName: string;
  description: string;
  defaultDurationSeconds: number;
  maxDurationSeconds: number;
  sensitive: boolean;
  enabled: boolean;
}

export interface DiagnosticJobDto {
  id: string;
  containerId: string;
  containerName: string;
  profile: string;
  status: string;
  progress: number;
  statusMessage: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  deadlineAt: string;
  createdBy: string;
  processId: number;
  runtimeMajor: number;
  runnerImage: string;
  toolVersion: string;
  artifactId: string | null;
  inputs: Record<string, unknown>;
  outcome: Record<string, unknown> | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface DiagnosticJobEventDto {
  id: number;
  jobId: string;
  timestamp: string;
  type: string;
  status: string;
  progress: number;
  message: string;
  metadata: Record<string, unknown> | null;
}

export interface GuidedIncidentProfileDto {
  id: string;
  displayName: string;
  description: string;
  counterPreset: string;
  primaryDiagnostic: string;
  optionalTrace: string | null;
  defaultCaptureSeconds: number;
}

export interface IncidentSummaryDto {
  id: string;
  containerId: string;
  containerName: string;
  processId: number;
  title: string;
  profile: string;
  reason: string | null;
  notes: string | null;
  status: string;
  progress: number;
  createdBy: string;
  createdAt: string;
  windowStart: string;
  windowEnd: string | null;
  completedAt: string | null;
  errorMessage: string | null;
}

export interface IncidentTimelineDto {
  id: number;
  timestamp: string;
  type: string;
  severity: string;
  title: string;
  summary: string;
  evidenceId: string | null;
  metadata: Record<string, unknown> | null;
}

export interface IncidentEvidenceDto {
  id: string;
  kind: string;
  title: string;
  capturedAt: string;
  from: string | null;
  to: string | null;
  sourceId: string | null;
  artifactId: string | null;
  summary: Record<string, unknown>;
  payload: unknown;
  selectedByDefault: boolean;
  sensitive: boolean;
  redactionStatus: string;
}

export interface IncidentFindingDto {
  id: string;
  code: string;
  severity: string;
  confidence: string;
  title: string;
  summary: string;
  createdAt: string;
  evidenceIds: string[];
}

export interface IncidentDetailDto {
  incident: IncidentSummaryDto;
  timeline: IncidentTimelineDto[];
  evidence: IncidentEvidenceDto[];
  findings: IncidentFindingDto[];
  analysis: AnalysisRunDto | null;
}

export interface AnalysisRunDto {
  id: string;
  incidentId: string;
  envelopeVersion: number;
  analyzerVersion: string;
  status: string;
  createdBy: string;
  createdAt: string;
  completedAt: string | null;
  errorMessage: string | null;
  envelope: AnalysisEnvelope | null;
}

export interface AnalysisEnvelope {
  schemaVersion: number;
  analyzerVersion: string;
  incidentId: string;
  generatedAt: string;
  window: { from: string; to: string };
  sources: Array<{ evidenceId: string; kind: string; title: string; artifactId: string | null }>;
  components: Array<{ name: string; status: string; durationMilliseconds: number; observationCount: number; error: string | null }>;
  observations: Array<{ id: string; analyzer: string; code: string; severity: string; confidence: string; title: string; summary: string; evidenceIds: string[]; data: unknown }>;
  correlations: Array<{ code: string; confidence: string; summary: string; observationIds: string[]; evidenceIds: string[] }>;
  limitations: Array<{ code: string; summary: string; evidenceId: string | null }>;
  disclosure: { localOnly: boolean; externalProvidersUsed: boolean; rawPayloadsIncluded: boolean };
}
