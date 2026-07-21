using Microsoft.EntityFrameworkCore;

namespace Tracebag.Api.Data;

public sealed class TracebagDbContext(DbContextOptions<TracebagDbContext> options) : DbContext(options)
{
    public DbSet<ContainerTargetRecord> ContainerTargets => Set<ContainerTargetRecord>();
    public DbSet<ContainerInstanceRecord> ContainerInstances => Set<ContainerInstanceRecord>();
    public DbSet<DockerEventRecord> DockerEvents => Set<DockerEventRecord>();
    public DbSet<ArtifactRecord> Artifacts => Set<ArtifactRecord>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<LogStreamRecord> LogStreams => Set<LogStreamRecord>();
    public DbSet<LogEntryRecord> LogEntries => Set<LogEntryRecord>();
    public DbSet<LogCheckpointRecord> LogCheckpoints => Set<LogCheckpointRecord>();
    public DbSet<CounterRecordingSessionRecord> CounterRecordingSessions => Set<CounterRecordingSessionRecord>();
    public DbSet<CounterSampleRecord> CounterSamples => Set<CounterSampleRecord>();
    public DbSet<CounterRollup1mRecord> CounterRollups1m => Set<CounterRollup1mRecord>();
    public DbSet<DiagnosticJobRecord> DiagnosticJobs => Set<DiagnosticJobRecord>();
    public DbSet<DiagnosticJobEventRecord> DiagnosticJobEvents => Set<DiagnosticJobEventRecord>();
    public DbSet<IncidentRecord> Incidents => Set<IncidentRecord>();
    public DbSet<IncidentTimelineRecord> IncidentTimeline => Set<IncidentTimelineRecord>();
    public DbSet<IncidentEvidenceRecord> IncidentEvidence => Set<IncidentEvidenceRecord>();
    public DbSet<IncidentFindingRecord> IncidentFindings => Set<IncidentFindingRecord>();
    public DbSet<IncidentFindingEvidenceRecord> IncidentFindingEvidence => Set<IncidentFindingEvidenceRecord>();
    public DbSet<AnalysisRunRecord> AnalysisRuns => Set<AnalysisRunRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContainerTargetRecord>(entity =>
        {
            entity.ToTable("container_targets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(128);
            entity.Property(x => x.IdentitySource).HasColumnName("identity_source").HasMaxLength(40).IsRequired();
            entity.Property(x => x.CurrentDockerId).HasColumnName("current_docker_id").HasMaxLength(128);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.ComposeProject).HasColumnName("compose_project").HasMaxLength(160);
            entity.Property(x => x.ComposeService).HasColumnName("compose_service").HasMaxLength(160);
            entity.Property(x => x.ComposeReplica).HasColumnName("compose_replica").HasMaxLength(40);
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Image).HasColumnName("image").HasMaxLength(300).IsRequired();
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
            entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
            entity.Property(x => x.Active).HasColumnName("active").IsRequired();
            entity.HasIndex(x => x.CurrentDockerId);
            entity.HasIndex(x => x.Active);
        });

        modelBuilder.Entity<ContainerInstanceRecord>(entity =>
        {
            entity.ToTable("container_instances");
            entity.HasKey(x => x.DockerId);
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128);
            entity.Property(x => x.ContainerTargetId).HasColumnName("container_target_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Image).HasColumnName("image").HasMaxLength(300).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at").IsRequired();
            entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
            entity.Property(x => x.RemovedAt).HasColumnName("removed_at");
            entity.HasOne(x => x.ContainerTarget).WithMany().HasForeignKey(x => x.ContainerTargetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.ContainerTargetId, x.CreatedAt });
        });

        modelBuilder.Entity<DockerEventRecord>(entity =>
        {
            entity.ToTable("docker_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ContainerTargetId).HasColumnName("container_target_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(x => x.AttributesJson).HasColumnName("attributes").HasColumnType("jsonb");
            entity.HasIndex(x => new { x.ContainerTargetId, x.Timestamp });
            entity.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.ToTable("artifacts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ContainerName).HasColumnName("container_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
            entity.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.Size).HasColumnName("size").IsRequired();
            entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(160).IsRequired();
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(x => x.DiagnosticJobId).HasColumnName("diagnostic_job_id").HasMaxLength(80);
            entity.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
            entity.Property(x => x.ManifestFileName).HasColumnName("manifest_file_name").HasMaxLength(500);
            entity.Property(x => x.State).HasColumnName("state").HasMaxLength(40).HasDefaultValue("available").IsRequired();
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.ExpiresAt);
            entity.HasIndex(x => x.ContainerId);
            entity.HasIndex(x => x.DiagnosticJobId).IsUnique();
        });

        modelBuilder.Entity<DiagnosticJobRecord>(entity =>
        {
            entity.ToTable("diagnostic_jobs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ContainerName).HasColumnName("container_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ProcessId).HasColumnName("process_id").IsRequired();
            entity.Property(x => x.Profile).HasColumnName("profile").HasMaxLength(60).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Progress).HasColumnName("progress").IsRequired();
            entity.Property(x => x.StatusMessage).HasColumnName("status_message").HasMaxLength(600);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.DeadlineAt).HasColumnName("deadline_at").IsRequired();
            entity.Property(x => x.CancelRequestedAt).HasColumnName("cancel_requested_at");
            entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(160).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(160);
            entity.Property(x => x.RequestFingerprint).HasColumnName("request_fingerprint").HasMaxLength(64).IsRequired();
            entity.Property(x => x.InputsJson).HasColumnName("inputs").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.OutcomeJson).HasColumnName("outcome").HasColumnType("jsonb");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(80);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1200);
            entity.Property(x => x.RunnerContainerId).HasColumnName("runner_container_id").HasMaxLength(128);
            entity.Property(x => x.RuntimeMajor).HasColumnName("runtime_major").IsRequired();
            entity.Property(x => x.RunnerImage).HasColumnName("runner_image").HasMaxLength(300).IsRequired();
            entity.Property(x => x.ToolVersion).HasColumnName("tool_version").HasMaxLength(40).IsRequired();
            entity.Property(x => x.ArtifactId).HasColumnName("artifact_id").HasMaxLength(80);
            entity.HasIndex(x => new { x.ContainerId, x.Status });
            entity.HasIndex(x => x.ContainerId)
                .HasDatabaseName("IX_diagnostic_jobs_one_active_target")
                .IsUnique()
                .HasFilter("status IN ('queued', 'validating', 'starting', 'running', 'collecting', 'stopping')");
            entity.HasIndex(x => new { x.CreatedAt, x.Status });
            entity.HasIndex(x => x.RunnerContainerId);
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.HasIndex(x => x.ArtifactId).IsUnique();
        });

        modelBuilder.Entity<DiagnosticJobEventRecord>(entity =>
        {
            entity.ToTable("diagnostic_job_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.JobId).HasColumnName("job_id").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Progress).HasColumnName("progress").IsRequired();
            entity.Property(x => x.Message).HasColumnName("message").HasMaxLength(600).IsRequired();
            entity.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            entity.HasOne(x => x.Job).WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.JobId, x.Id });
            entity.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(x => x.User).HasColumnName("user").HasMaxLength(160).IsRequired();
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(120).IsRequired();
            entity.Property(x => x.TargetContainerId).HasColumnName("target_container_id").HasMaxLength(128);
            entity.Property(x => x.TargetContainerName).HasColumnName("target_container_name").HasMaxLength(200);
            entity.Property(x => x.Result).HasColumnName("result").HasMaxLength(40).IsRequired();
            entity.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            entity.HasIndex(x => x.Timestamp);
            entity.HasIndex(x => x.Action);
            entity.HasIndex(x => x.TargetContainerId);
        });

        modelBuilder.Entity<LogStreamRecord>(entity =>
        {
            entity.ToTable("log_streams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.CurrentDockerId).HasColumnName("current_docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ContainerName).HasColumnName("container_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Image).HasColumnName("image").HasMaxLength(300);
            entity.Property(x => x.Parser).HasColumnName("parser").HasMaxLength(20).IsRequired();
            entity.Property(x => x.RetentionDays).HasColumnName("retention_days").IsRequired();
            entity.Property(x => x.MaxBytes).HasColumnName("max_bytes").IsRequired();
            entity.Property(x => x.Active).HasColumnName("active").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            entity.Property(x => x.LabelsJson).HasColumnName("labels").HasColumnType("jsonb");
            entity.HasIndex(x => x.ContainerId).IsUnique();
            entity.HasIndex(x => x.CurrentDockerId);
        });

        modelBuilder.Entity<LogEntryRecord>(entity =>
        {
            entity.ToTable("log_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.LogStreamId).HasColumnName("log_stream_id").IsRequired();
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ReceivedAt).HasColumnName("received_at").IsRequired();
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired();
            entity.Property(x => x.SourceTimestamp).HasColumnName("source_timestamp").HasMaxLength(60).IsRequired();
            entity.Property(x => x.Stream).HasColumnName("stream").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Line).HasColumnName("line").IsRequired();
            entity.Property(x => x.Message).HasColumnName("message").IsRequired();
            entity.Property(x => x.Level).HasColumnName("level").HasMaxLength(40);
            entity.Property(x => x.ExceptionType).HasColumnName("exception_type").HasMaxLength(240);
            entity.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(128);
            entity.Property(x => x.ParsedJson).HasColumnName("parsed_json").HasColumnType("jsonb");
            entity.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64).IsRequired();
            entity.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
            if (Database.IsNpgsql())
            {
                entity.Property(x => x.SearchVector).HasColumnName("search_vector");
                entity.HasGeneratedTsVectorColumn(
                    x => x.SearchVector,
                    "simple",
                    x => new { x.Message, x.Line });
                entity.HasIndex(x => x.SearchVector).HasMethod("GIN");
            }
            else
            {
                entity.Ignore(x => x.SearchVector);
            }
            entity.HasOne(x => x.LogStream).WithMany().HasForeignKey(x => x.LogStreamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.ReceivedAt);
            entity.HasIndex(x => new { x.ContainerId, x.Timestamp, x.Id });
            entity.HasIndex(x => x.Level);
            entity.HasIndex(x => x.TraceId);
            entity.HasIndex(x => x.Fingerprint).IsUnique();
        });

        modelBuilder.Entity<LogCheckpointRecord>(entity =>
        {
            entity.ToTable("log_checkpoints");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.LastTimestamp).HasColumnName("last_timestamp");
            entity.Property(x => x.LastFingerprint).HasColumnName("last_fingerprint").HasMaxLength(64);
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(x => x.ContainerId).IsUnique();
        });

        modelBuilder.Entity<CounterRecordingSessionRecord>(entity =>
        {
            entity.ToTable("counter_recording_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ContainerName).HasColumnName("container_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(4000);
            entity.Property(x => x.RunnerContainerId).HasColumnName("runner_container_id").HasMaxLength(128);
            entity.Property(x => x.ProcessId).HasColumnName("process_id").IsRequired();
            entity.Property(x => x.Preset).HasColumnName("preset").HasMaxLength(80).IsRequired();
            entity.Property(x => x.IntervalSeconds).HasColumnName("interval_seconds").IsRequired();
            entity.Property(x => x.MaxDurationSeconds).HasColumnName("max_duration_seconds").IsRequired();
            entity.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            entity.Property(x => x.StoppedAt).HasColumnName("stopped_at");
            entity.Property(x => x.LastSampleAt).HasColumnName("last_sample_at");
            entity.Property(x => x.SampleCount).HasColumnName("sample_count").IsRequired();
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
            entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(160).IsRequired();
            entity.Property(x => x.StopReason).HasColumnName("stop_reason").HasMaxLength(80);
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(600);
            entity.Property(x => x.ProvidersJson).HasColumnName("providers").HasColumnType("jsonb");
            entity.Property(x => x.RuntimeMajor).HasColumnName("runtime_major").IsRequired();
            entity.Property(x => x.RunnerImage).HasColumnName("runner_image").HasMaxLength(300).IsRequired();
            entity.Property(x => x.ToolVersion).HasColumnName("tool_version").HasMaxLength(40).IsRequired();
            entity.HasIndex(x => x.ContainerId)
                .HasDatabaseName("IX_counter_recording_sessions_one_active_target")
                .IsUnique()
                .HasFilter("status IN ('starting', 'running', 'stopping')");
            entity.HasIndex(x => x.StartedAt);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<CounterSampleRecord>(entity =>
        {
            entity.ToTable("counter_samples");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(80).IsRequired();
            entity.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            entity.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(160).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(240).IsRequired();
            entity.Property(x => x.CounterType).HasColumnName("counter_type").HasMaxLength(120).IsRequired();
            entity.Property(x => x.Value).HasColumnName("value").IsRequired();
            entity.Property(x => x.TagsJson).HasColumnName("tags").HasColumnType("jsonb");
            entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SessionId, x.CapturedAt });
            entity.HasIndex(x => new { x.Provider, x.Name });
        });

        modelBuilder.Entity<CounterRollup1mRecord>(entity =>
        {
            entity.ToTable("counter_rollups_1m");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SessionId).HasColumnName("session_id").HasMaxLength(80).IsRequired();
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(160).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(240).IsRequired();
            entity.Property(x => x.CounterType).HasColumnName("counter_type").HasMaxLength(120).IsRequired();
            entity.Property(x => x.BucketStart).HasColumnName("bucket_start").IsRequired();
            entity.Property(x => x.Average).HasColumnName("average").IsRequired();
            entity.Property(x => x.Minimum).HasColumnName("minimum").IsRequired();
            entity.Property(x => x.Maximum).HasColumnName("maximum").IsRequired();
            entity.Property(x => x.Count).HasColumnName("count").IsRequired();
            entity.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.SessionId, x.Provider, x.Name, x.CounterType, x.BucketStart }).IsUnique();
            entity.HasIndex(x => new { x.ContainerId, x.BucketStart });
            entity.HasIndex(x => new { x.Provider, x.Name });
        });

        modelBuilder.Entity<IncidentRecord>(entity =>
        {
            entity.ToTable("incidents"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.ContainerId).HasColumnName("container_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ContainerName).HasColumnName("container_name").HasMaxLength(200).IsRequired();
            entity.Property(x => x.DockerId).HasColumnName("docker_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.ProcessId).HasColumnName("process_id").IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Profile).HasColumnName("profile").HasMaxLength(60).IsRequired();
            entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2000);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(8000);
            entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Progress).HasColumnName("progress").IsRequired();
            entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(160).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.WindowStart).HasColumnName("window_start").IsRequired();
            entity.Property(x => x.WindowEnd).HasColumnName("window_end");
            entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1200);
            entity.Property(x => x.CaptureOptionsJson).HasColumnName("capture_options").HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.CreatedAt, x.Status });
            entity.HasIndex(x => x.ContainerId).HasDatabaseName("IX_incidents_one_active_target").IsUnique().HasFilter("status IN ('queued', 'collecting', 'analyzing')");
        });

        modelBuilder.Entity<IncidentEvidenceRecord>(entity =>
        {
            entity.ToTable("incident_evidence"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.IncidentId).HasColumnName("incident_id").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            entity.Property(x => x.From).HasColumnName("from_at"); entity.Property(x => x.To).HasColumnName("to_at");
            entity.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(100);
            entity.Property(x => x.ArtifactId).HasColumnName("artifact_id").HasMaxLength(80);
            entity.Property(x => x.SummaryJson).HasColumnName("summary").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.SelectedByDefault).HasColumnName("selected_by_default").IsRequired();
            entity.Property(x => x.Sensitive).HasColumnName("sensitive").IsRequired();
            entity.Property(x => x.RedactionStatus).HasColumnName("redaction_status").HasMaxLength(40).IsRequired();
            entity.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.IncidentId, x.CapturedAt }); entity.HasIndex(x => x.ArtifactId);
            entity.HasIndex(x => new { x.Kind, x.SourceId });
        });

        modelBuilder.Entity<IncidentTimelineRecord>(entity =>
        {
            entity.ToTable("incident_timeline"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id"); entity.Property(x => x.IncidentId).HasColumnName("incident_id").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").IsRequired(); entity.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
            entity.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired(); entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired(); entity.Property(x => x.EvidenceId).HasColumnName("evidence_id").HasMaxLength(80);
            entity.Property(x => x.MetadataJson).HasColumnName("metadata").HasColumnType("jsonb");
            entity.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.IncidentId, x.Timestamp });
        });

        modelBuilder.Entity<IncidentFindingRecord>(entity =>
        {
            entity.ToTable("incident_findings"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.IncidentId).HasColumnName("incident_id").HasMaxLength(80).IsRequired(); entity.Property(x => x.AnalysisRunId).HasColumnName("analysis_run_id").HasMaxLength(80); entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(80).IsRequired();
            entity.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired(); entity.Property(x => x.Confidence).HasColumnName("confidence").HasMaxLength(20).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired(); entity.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired(); entity.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.IncidentId, x.CreatedAt });
            entity.HasOne(x => x.AnalysisRun).WithMany().HasForeignKey(x => x.AnalysisRunId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AnalysisRunRecord>(entity =>
        {
            entity.ToTable("analysis_runs"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(80);
            entity.Property(x => x.IncidentId).HasColumnName("incident_id").HasMaxLength(80).IsRequired(); entity.Property(x => x.EnvelopeVersion).HasColumnName("envelope_version").IsRequired();
            entity.Property(x => x.AnalyzerVersion).HasColumnName("analyzer_version").HasMaxLength(80).IsRequired(); entity.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(200).IsRequired(); entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired(); entity.Property(x => x.CompletedAt).HasColumnName("completed_at");
            entity.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000); entity.Property(x => x.EnvelopeJson).HasColumnName("envelope").HasColumnType("jsonb");
            entity.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade); entity.HasIndex(x => new { x.IncidentId, x.CreatedAt });
            entity.HasIndex(x => x.IncidentId).IsUnique().HasFilter("status = 'running'").HasDatabaseName("IX_analysis_runs_one_active_incident");
        });

        modelBuilder.Entity<IncidentFindingEvidenceRecord>(entity =>
        {
            entity.ToTable("incident_finding_evidence"); entity.HasKey(x => new { x.FindingId, x.EvidenceId });
            entity.Property(x => x.FindingId).HasColumnName("finding_id").HasMaxLength(80); entity.Property(x => x.EvidenceId).HasColumnName("evidence_id").HasMaxLength(80);
            entity.HasOne(x => x.Finding).WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Evidence).WithMany().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
