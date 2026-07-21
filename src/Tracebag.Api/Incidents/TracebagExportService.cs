using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tracebag.Api.Artifacts;
using Tracebag.Api.Models;
using Tracebag.Api.Security;

namespace Tracebag.Api.Incidents;

public sealed class TracebagExportService(
    IncidentService incidents,
    ArtifactStore artifacts)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task WriteAsync(string incidentId, TracebagExportSelection selection, Stream destination, CancellationToken cancellationToken)
    {
        var detail = await incidents.GetAsync(incidentId, cancellationToken);
        if (detail.Incident.Status is "queued" or "collecting" or "analyzing")
        {
            throw new TracebagException(409, "incident_export_not_ready", "Wait for the incident capture to finish before exporting it.");
        }

        var (requested, artifactEvidence) = TracebagBundlePolicy.Validate(detail.Evidence, selection);

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"tracebag-export-{Guid.NewGuid():N}.zip");
        try
        {
            await using var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var zip = new ZipArchive(temporary, ZipArchiveMode.Create, leaveOpen: true);
            var checksums = new List<object>();
            await AddTextAsync(zip, "README.md", BuildReadme(detail, selection), checksums, cancellationToken);
            await AddJsonAsync(zip, "incident.json", detail.Incident, checksums, cancellationToken);
            await AddJsonAsync(zip, "timeline.json", detail.Timeline, checksums, cancellationToken);
            await AddJsonAsync(zip, "findings.json", detail.Findings, checksums, cancellationToken);
            if (detail.Analysis?.Envelope is not null)
            {
                await AddJsonAsync(zip, "analysis-envelope.json", detail.Analysis.Envelope, checksums, cancellationToken);
            }
            foreach (var evidence in detail.Evidence)
            {
                await AddJsonAsync(zip, $"evidence/{Safe(evidence.Id)}.json", new
                {
                    evidence.Id,
                    evidence.Kind,
                    evidence.Title,
                    evidence.CapturedAt,
                    evidence.From,
                    evidence.To,
                    evidence.SourceId,
                    evidence.ArtifactId,
                    evidence.Summary,
                    evidence.Sensitive,
                    evidence.RedactionStatus,
                    payloadIncluded = false
                }, checksums, cancellationToken);
            }
            if (selection.IncludePinnedLogs)
            {
                foreach (var evidence in detail.Evidence.Where(x => x.Kind == "logs"))
                {
                    await AddJsonAsync(zip, $"raw/{Safe(evidence.Id)}-pinned-logs.json", evidence.Payload, checksums, cancellationToken);
                }
            }
            foreach (var evidence in artifactEvidence)
            {
                var artifactId = evidence.ArtifactId!;
                var (metadata, path) = await artifacts.GetForDownloadAsync(artifactId, cancellationToken);
                var entryName = $"artifacts/{Safe(artifactId)}/{Safe(Path.GetFileName(metadata.FileName))}";
                var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024]; int read;
                await using (var output = entry.Open())
                {
                    while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        hash.AppendData(buffer, 0, read);
                    }
                }
                checksums.Add(new { path = entryName, sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), size = metadata.Size, sourceSha256 = metadata.Sha256 });
                if (!string.IsNullOrWhiteSpace(metadata.ManifestFileName))
                {
                    var (_, manifestPath) = await artifacts.GetManifestAsync(artifactId, cancellationToken);
                    await AddFileAsync(zip, $"artifacts/{Safe(artifactId)}/manifest.json", manifestPath, checksums, cancellationToken);
                }
            }
            var manifest = new
            {
                format = "tracebag",
                version = 1,
                incidentId,
                createdAt = DateTimeOffset.UtcNow,
                selfContained = true,
                selection = new { selection.IncludePinnedLogs, artifactIds = requested.Order().ToArray(), selection.IncludeSensitiveArtifacts },
                safety = new { historicalLogsIncluded = false, pinnedLogsIncluded = selection.IncludePinnedLogs, fullDumpIncluded = artifactEvidence.Any(x => x.Sensitive), artifactsRequireExactSelection = true },
                redaction = new { status = selection.IncludePinnedLogs || artifactEvidence.Length > 0 ? "contains-unredacted-selected-raw-evidence" : "summaries-only", evidence = detail.Evidence.Select(x => new { x.Id, x.RedactionStatus, summaryIncluded = true, rawIncluded = x.Kind == "logs" && selection.IncludePinnedLogs || x.ArtifactId is not null && requested.Contains(x.ArtifactId) }) },
                checksums
            };
            await AddJsonAsync(zip, "manifest.json", manifest, null, cancellationToken);
            zip.Dispose();
            temporary.Position = 0;
            await temporary.CopyToAsync(destination, 128 * 1024, cancellationToken);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string BuildReadme(IncidentDetailDto detail, TracebagExportSelection selection) => $"""
        # Tracebag: {detail.Incident.Title}

        Incident `{detail.Incident.Id}` captured `{detail.Incident.Profile}` evidence for `{detail.Incident.ContainerName}`.

        Status: {detail.Incident.Status}
        Window: {detail.Incident.WindowStart:O} — {detail.Incident.WindowEnd:O}

        Start with `incident.json`, then read `timeline.json`, `analysis-envelope.json`, and `findings.json`. The versioned local analysis records confidence, limitations, correlations, and exact evidence IDs. Evidence snapshots are under `evidence/`.

        This export contains only bounded incident evidence. Pinned raw logs: {(selection.IncludePinnedLogs ? "included by explicit selection" : "not included")}. Selected raw artifacts: {selection.ArtifactIds.Count}.
        `manifest.json` records selection, redaction state and SHA-256 checksums. A full process dump is never added implicitly.
        """;
    private static async Task AddJsonAsync(ZipArchive zip, string name, object value, List<object>? checksums, CancellationToken ct) => await AddBytesAsync(zip, name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), checksums, ct);
    private static async Task AddTextAsync(ZipArchive zip, string name, string value, List<object>? checksums, CancellationToken ct) => await AddBytesAsync(zip, name, Encoding.UTF8.GetBytes(value), checksums, ct);
    private static async Task AddBytesAsync(ZipArchive zip, string name, byte[] bytes, List<object>? checksums, CancellationToken ct) { var entry = zip.CreateEntry(name, CompressionLevel.Fastest); await using var output = entry.Open(); await output.WriteAsync(bytes, ct); checksums?.Add(new { path = name, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), size = bytes.LongLength }); }
    private static async Task AddFileAsync(ZipArchive zip, string name, string path, List<object> checksums, CancellationToken ct) { var bytes = await File.ReadAllBytesAsync(path, ct); await AddBytesAsync(zip, name, bytes, checksums, ct); }
    private static string Safe(string value) => string.Concat(value.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_'));
}

public static class TracebagBundlePolicy
{
    public static (HashSet<string> Requested, IncidentEvidenceDto[] Selected) Validate(
        IReadOnlyList<IncidentEvidenceDto> evidence,
        TracebagExportSelection selection)
    {
        var requested = new HashSet<string>(selection.ArtifactIds ?? [], StringComparer.Ordinal);
        var selected = evidence.Where(x => x.ArtifactId is not null && requested.Contains(x.ArtifactId)).ToArray();
        if (selected.Length != requested.Count)
        {
            throw new TracebagException(400, "incident_export_artifact_invalid", "Every selected artifact must belong to this incident.");
        }

        if (!selection.IncludeSensitiveArtifacts && selected.Any(x => x.Sensitive))
        {
            throw new TracebagException(400, "incident_export_sensitive_confirmation_required", "Sensitive artifacts require the explicit includeSensitiveArtifacts flag.");
        }

        return (requested, selected);
    }
}
