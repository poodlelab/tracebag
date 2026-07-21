using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tracebag.Api.Auth;
using Tracebag.Api.Data;
using Tracebag.Api.Security;

namespace Tracebag.Api.Artifacts;

public sealed class ArtifactStore(TracebagOptions options, IDbContextFactory<TracebagDbContext>? dbContextFactory = null) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly TracebagOptions _options = options;
    private readonly IDbContextFactory<TracebagDbContext>? _dbContextFactory = dbContextFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<ArtifactMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is not null)
        {
            await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            ArtifactRecord[] artifacts = await db.Artifacts
                .AsNoTracking()
                .OrderByDescending(artifact => artifact.CreatedAt)
                .ToArrayAsync(cancellationToken);
            return [.. artifacts.Select(ToMetadata)];
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return [.. (await ReadUnsafeAsync(cancellationToken)).OrderByDescending(artifact => artifact.CreatedAt)];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ArtifactMetadata> RegisterAsync(
        string id,
        string containerId,
        string containerName,
        string type,
        string fileName,
        string createdBy,
        CancellationToken cancellationToken)
    {
        string path = GetArtifactPath(fileName);
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new TracebagException(StatusCodes.Status500InternalServerError, "artifact_missing", "The diagnostic command completed but did not produce the expected artifact.");
        }

        var metadata = new ArtifactMetadata(
            id,
            containerId,
            containerName,
            type,
            fileName,
            DateTimeOffset.UtcNow,
            file.Length,
            string.IsNullOrWhiteSpace(createdBy) ? "anonymous" : createdBy,
            DateTimeOffset.UtcNow.AddHours(_options.ArtifactRetentionHours));

        if (_dbContextFactory is not null)
        {
            await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            ArtifactRecord? existing = await db.Artifacts.FindAsync([id], cancellationToken);
            if (existing is null)
            {
                db.Artifacts.Add(ToRecord(metadata));
            }
            else
            {
                UpdateRecord(existing, metadata);
            }

            await db.SaveChangesAsync(cancellationToken);
            return metadata;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var artifacts = (await ReadUnsafeAsync(cancellationToken)).ToList();
            artifacts.RemoveAll(artifact => artifact.Id == id);
            artifacts.Add(metadata);
            await WriteUnsafeAsync(artifacts, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }

        return metadata;
    }

    public async Task<ArtifactMetadata> RegisterJobArtifactAsync(
        string artifactId,
        string diagnosticJobId,
        string containerId,
        string containerName,
        string profile,
        string stagingFileName,
        string extension,
        string createdBy,
        int processId,
        int runtimeMajor,
        string runnerImage,
        string toolVersion,
        object inputs,
        object outcome,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            throw new InvalidOperationException("Diagnostic job artifacts require database persistence.");
        }

        string stagingPath = GetArtifactPath(stagingFileName);
        if (!File.Exists(stagingPath))
        {
            throw new TracebagException(StatusCodes.Status500InternalServerError, "artifact_missing", "The diagnostic runner completed but did not produce its expected output.");
        }

        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        string relativeDirectory = Path.Combine(createdAt.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture), artifactId);
        string absoluteDirectory = GetArtifactPath(relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);
        string payloadName = $"payload.{extension}";
        string payloadRelativePath = Path.Combine(relativeDirectory, payloadName).Replace(Path.DirectorySeparatorChar, '/');
        string payloadPath = GetArtifactPath(payloadRelativePath);
        if (File.Exists(payloadPath))
        {
            throw new TracebagException(StatusCodes.Status409Conflict, "artifact_exists", "The artifact output already exists.");
        }

        File.Move(stagingPath, payloadPath);
        try
        {
            var file = new FileInfo(payloadPath);
            string sha256;
            await using (var stream = new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            }

            string manifestRelativePath = Path.Combine(relativeDirectory, "manifest.json").Replace(Path.DirectorySeparatorChar, '/');
            string manifestPath = GetArtifactPath(manifestRelativePath);
            var manifest = new ArtifactManifest(
                1,
                artifactId,
                diagnosticJobId,
                containerId,
                containerName,
                profile,
                payloadName,
                file.Length,
                sha256,
                createdAt,
                string.IsNullOrWhiteSpace(createdBy) ? "anonymous" : createdBy,
                processId,
                runtimeMajor,
                runnerImage,
                toolVersion,
                inputs,
                outcome);
            string temporaryManifest = manifestPath + ".tmp";
            await using (FileStream stream = File.Create(temporaryManifest))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
            }
            File.Move(temporaryManifest, manifestPath, overwrite: true);

            var metadata = new ArtifactMetadata(
                artifactId,
                containerId,
                containerName,
                profile,
                payloadRelativePath,
                createdAt,
                file.Length,
                string.IsNullOrWhiteSpace(createdBy) ? "anonymous" : createdBy,
                createdAt.AddHours(_options.ArtifactRetentionHours),
                diagnosticJobId,
                sha256,
                manifestRelativePath,
                "available");
            await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            db.Artifacts.Add(ToRecord(metadata));
            await db.SaveChangesAsync(cancellationToken);
            return metadata;
        }
        catch
        {
            if (Directory.Exists(absoluteDirectory))
            {
                Directory.Delete(absoluteDirectory, recursive: true);
            }

            throw;
        }
    }

    public async Task<(ArtifactMetadata Metadata, string Path)> GetForDownloadAsync(string artifactId, CancellationToken cancellationToken)
    {
        ArtifactMetadata metadata = await GetAsync(artifactId, cancellationToken);
        string path = GetArtifactPath(metadata.FileName);
        if (!File.Exists(path))
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "artifact_file_missing", "The artifact file no longer exists.");
        }
        EnsureNoSymbolicLinks(path);

        return (metadata, path);
    }

    public async Task<(ArtifactMetadata Metadata, string Path)> GetManifestAsync(string artifactId, CancellationToken cancellationToken)
    {
        ArtifactMetadata metadata = await GetAsync(artifactId, cancellationToken);
        if (string.IsNullOrWhiteSpace(metadata.ManifestFileName))
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "artifact_manifest_not_found", "This legacy artifact does not have a manifest.");
        }

        string path = GetArtifactPath(metadata.ManifestFileName);
        if (!File.Exists(path))
        {
            throw new TracebagException(StatusCodes.Status404NotFound, "artifact_manifest_missing", "The artifact manifest file no longer exists.");
        }

        EnsureNoSymbolicLinks(path);
        return (metadata, path);
    }

    public async Task<ArtifactReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return new ArtifactReconciliationResult(0, 0);
        }

        await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        List<ArtifactRecord> records = await db.Artifacts.ToListAsync(cancellationToken);
        var knownPaths = new HashSet<string>(StringComparer.Ordinal);
        int missing = 0;
        foreach (ArtifactRecord? record in records)
        {
            knownPaths.Add(NormalizeRelative(record.FileName));
            if (!string.IsNullOrWhiteSpace(record.ManifestFileName))
            {
                knownPaths.Add(NormalizeRelative(record.ManifestFileName));
            }

            bool exists = File.Exists(GetArtifactPath(record.FileName));
            string desired = exists ? "available" : "missing";
            if (!exists)
            {
                missing++;
            }

            record.State = desired;
        }
        await db.SaveChangesAsync(cancellationToken);

        int quarantined = 0;
        string quarantineRoot = Path.Combine(_options.ArtifactDir, "quarantine");
        foreach (string? file in Directory.EnumerateFiles(_options.ArtifactDir, "*", SearchOption.AllDirectories).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relative = NormalizeRelative(Path.GetRelativePath(_options.ArtifactDir, file));
            if (relative.StartsWith("quarantine/", StringComparison.Ordinal) || knownPaths.Contains(relative))
            {
                continue;
            }

            string quarantineDirectory = Path.Combine(quarantineRoot, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(quarantineDirectory);
            string destination = Path.Combine(quarantineDirectory, $"{Guid.NewGuid():N}-{Path.GetFileName(file)}");
            File.Move(file, destination);
            quarantined++;
        }

        return new ArtifactReconciliationResult(missing, quarantined);
    }

    public async Task<ArtifactMetadata> DeleteAsync(string artifactId, CancellationToken cancellationToken)
    {
        if (_dbContextFactory is not null)
        {
            await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            ArtifactRecord record = await db.Artifacts.FirstOrDefaultAsync(artifact => artifact.Id == artifactId, cancellationToken)
                ?? throw new TracebagException(StatusCodes.Status404NotFound, "artifact_not_found", "The requested artifact was not found.");
            if (await db.IncidentEvidence.AnyAsync(evidence => evidence.ArtifactId == artifactId, cancellationToken))
            {
                throw new TracebagException(StatusCodes.Status409Conflict, "artifact_referenced_by_incident", "This artifact is evidence in an incident and cannot be deleted independently.");
            }
            ArtifactMetadata metadata = ToMetadata(record);
            DeleteArtifactFiles(metadata);
            db.Artifacts.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            return metadata;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var artifacts = (await ReadUnsafeAsync(cancellationToken)).ToList();
            ArtifactMetadata metadata = artifacts.FirstOrDefault(artifact => artifact.Id == artifactId)
                ?? throw new TracebagException(StatusCodes.Status404NotFound, "artifact_not_found", "The requested artifact was not found.");
            artifacts.Remove(metadata);
            string path = GetArtifactPath(metadata.FileName);
            if (File.Exists(path))
            {
                DeleteArtifactFiles(metadata);
            }

            await WriteUnsafeAsync(artifacts, cancellationToken);
            return metadata;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is not null)
        {
            await ApplyDatabaseRetentionAsync(cancellationToken);
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var artifacts = (await ReadUnsafeAsync(cancellationToken))
                .OrderBy(artifact => artifact.CreatedAt)
                .ToList();

            var kept = new List<ArtifactMetadata>();
            foreach (ArtifactMetadata? artifact in artifacts)
            {
                if (artifact.ExpiresAt <= now)
                {
                    DeleteArtifactFiles(artifact);
                    continue;
                }

                kept.Add(artifact);
            }

            while (kept.Count > _options.ArtifactMaxCount)
            {
                DeleteArtifactFiles(kept[0]);
                kept.RemoveAt(0);
            }

            while (kept.Sum(artifact => artifact.Size) > _options.ArtifactMaxTotalBytes && kept.Count > 0)
            {
                DeleteArtifactFiles(kept[0]);
                kept.RemoveAt(0);
            }

            await WriteUnsafeAsync(kept, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public string CreateFileName(string type, string containerName, string extension)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string safeContainer = string.Concat(containerName.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-')).Trim('-');
        if (string.IsNullOrWhiteSpace(safeContainer))
        {
            safeContainer = "container";
        }

        return $"{timestamp}-{safeContainer}-{type}.{extension}";
    }

    private async Task<ArtifactMetadata> GetAsync(string artifactId, CancellationToken cancellationToken)
    {
        if (_dbContextFactory is not null)
        {
            await using TracebagDbContext db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            ArtifactRecord record = await db.Artifacts
                .AsNoTracking()
                .FirstOrDefaultAsync(artifact => artifact.Id == artifactId, cancellationToken)
                ?? throw new TracebagException(StatusCodes.Status404NotFound, "artifact_not_found", "The requested artifact was not found.");
            return ToMetadata(record);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await ReadUnsafeAsync(cancellationToken)).FirstOrDefault(artifact => artifact.Id == artifactId)
                ?? throw new TracebagException(StatusCodes.Status404NotFound, "artifact_not_found", "The requested artifact was not found.");
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ApplyDatabaseRetentionAsync(CancellationToken cancellationToken)
    {
        await using TracebagDbContext db = await _dbContextFactory!.CreateDbContextAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ArtifactRecord> artifacts = await db.Artifacts
            .OrderBy(artifact => artifact.CreatedAt)
            .ToListAsync(cancellationToken);
        HashSet<string> referencedArtifactIds = (await db.IncidentEvidence
            .Where(evidence => evidence.ArtifactId != null)
            .Select(evidence => evidence.ArtifactId!)
            .ToArrayAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var toDelete = artifacts
            .Where(artifact => artifact.ExpiresAt <= now && !referencedArtifactIds.Contains(artifact.Id))
            .ToList();
        var kept = artifacts.Except(toDelete).ToList();

        while (kept.Count > _options.ArtifactMaxCount)
        {
            ArtifactRecord? candidate = kept.FirstOrDefault(artifact => !referencedArtifactIds.Contains(artifact.Id));
            if (candidate is null)
            {
                break;
            }
            toDelete.Add(candidate);
            kept.Remove(candidate);
        }

        while (kept.Sum(artifact => artifact.Size) > _options.ArtifactMaxTotalBytes && kept.Count > 0)
        {
            ArtifactRecord? candidate = kept.FirstOrDefault(artifact => !referencedArtifactIds.Contains(artifact.Id));
            if (candidate is null)
            {
                break;
            }
            toDelete.Add(candidate);
            kept.Remove(candidate);
        }

        foreach (ArtifactRecord? artifact in toDelete)
        {
            DeleteArtifactFiles(ToMetadata(artifact));
        }

        if (toDelete.Count > 0)
        {
            db.Artifacts.RemoveRange(toDelete);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ArtifactMetadata>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        string path = GetMetadataPath();
        if (!File.Exists(path))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ArtifactMetadata>>(stream, JsonOptions, cancellationToken)
            ?? [];
    }

    private async Task WriteUnsafeAsync(IReadOnlyList<ArtifactMetadata> artifacts, CancellationToken cancellationToken)
    {
        string path = GetMetadataPath();
        string tempPath = path + ".tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, artifacts, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string GetMetadataPath()
    {
        return Path.Combine(_options.DataDir, "artifacts.json");
    }

    internal string GetArtifactPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "artifact_filename_invalid", "The artifact file name is invalid.");
        }
        string root = Path.GetFullPath(_options.ArtifactDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, fileName));
        if (!candidate.StartsWith(root, StringComparison.Ordinal))
        {
            throw new TracebagException(StatusCodes.Status400BadRequest, "artifact_filename_invalid", "The artifact file name escapes the artifact storage root.");
        }
        return candidate;
    }

    private void DeleteFileIfExists(string fileName)
    {
        string path = GetArtifactPath(fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void DeleteArtifactFiles(ArtifactMetadata artifact)
    {
        DeleteFileIfExists(artifact.FileName);
        if (!string.IsNullOrWhiteSpace(artifact.ManifestFileName))
        {
            DeleteFileIfExists(artifact.ManifestFileName);
        }

        string? directory = Path.GetDirectoryName(GetArtifactPath(artifact.FileName));
        if (!string.IsNullOrWhiteSpace(directory)
            && !string.Equals(Path.GetFullPath(directory), Path.GetFullPath(_options.ArtifactDir), StringComparison.Ordinal)
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static string NormalizeRelative(string value) => value.Replace('\\', '/').TrimStart('/');

    private void EnsureNoSymbolicLinks(string path)
    {
        string root = Path.GetFullPath(_options.ArtifactDir).TrimEnd(Path.DirectorySeparatorChar);
        FileSystemInfo current = new FileInfo(path);
        while (!string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.Ordinal))
        {
            if (current.LinkTarget is not null)
            {
                throw new TracebagException(StatusCodes.Status400BadRequest, "artifact_path_link_invalid", "Artifact downloads cannot traverse symbolic links.");
            }
            current = current switch
            {
                FileInfo file => file.Directory ?? breakLoop(),
                DirectoryInfo directory => directory.Parent ?? breakLoop(),
                _ => breakLoop()
            };
        }

        static FileSystemInfo breakLoop() => throw new TracebagException(StatusCodes.Status400BadRequest, "artifact_path_invalid", "The artifact path is invalid.");
    }

    private static ArtifactMetadata ToMetadata(ArtifactRecord artifact)
    {
        return new ArtifactMetadata(
            artifact.Id,
            artifact.ContainerId,
            artifact.ContainerName,
            artifact.Type,
            artifact.FileName,
            artifact.CreatedAt,
            artifact.Size,
            artifact.CreatedBy,
            artifact.ExpiresAt,
            artifact.DiagnosticJobId,
            artifact.Sha256,
            artifact.ManifestFileName,
            artifact.State);
    }

    private static ArtifactRecord ToRecord(ArtifactMetadata artifact)
    {
        var record = new ArtifactRecord();
        UpdateRecord(record, artifact);
        return record;
    }

    private static void UpdateRecord(ArtifactRecord record, ArtifactMetadata artifact)
    {
        record.Id = artifact.Id;
        record.ContainerId = artifact.ContainerId;
        record.ContainerName = artifact.ContainerName;
        record.Type = artifact.Type;
        record.FileName = artifact.FileName;
        record.CreatedAt = artifact.CreatedAt;
        record.Size = artifact.Size;
        record.CreatedBy = artifact.CreatedBy;
        record.ExpiresAt = artifact.ExpiresAt;
        record.DiagnosticJobId = artifact.DiagnosticJobId;
        record.Sha256 = artifact.Sha256;
        record.ManifestFileName = artifact.ManifestFileName;
        record.State = artifact.State;
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}

public sealed record ArtifactReconciliationResult(int MissingFiles, int QuarantinedFiles);
