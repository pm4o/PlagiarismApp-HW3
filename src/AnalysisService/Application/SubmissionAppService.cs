using System.Security.Cryptography;
using System.Text.Json;
using AnalysisService.Data;
using AnalysisService.Domain;
using AnalysisService.Infra;
using Microsoft.EntityFrameworkCore;

namespace AnalysisService.Application;

public sealed class SubmissionAppService
{
    private readonly AppDbContext _db;
    private readonly FileClient _files;
    private readonly QuickChartClient _qc;
    private readonly ILogger<SubmissionAppService> _log;
    private readonly bool _enableWordCloud;

    public SubmissionAppService(
        AppDbContext db,
        FileClient files,
        QuickChartClient qc,
        ILogger<SubmissionAppService> log,
        bool enableWordCloud)
    {
        _db = db;
        _files = files;
        _qc = qc;
        _log = log;
        _enableWordCloud = enableWordCloud;
    }

    public async Task<SubmissionStartResponse> StartAsync(SubmissionStartRequest req, CancellationToken ct)
    {
        ValidateStart(req);

        var existing = await _db.Submissions.FirstOrDefaultAsync(x => x.IdempotencyKey == req.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, req.RequestHash, StringComparison.Ordinal))
                throw new InvalidOperationException("IDEMPOTENCY_KEY_CONFLICT");

            var work = await _db.Works.FirstOrDefaultAsync(w => w.Id == existing.WorkId, ct);
            var ws = work?.Status.ToString() ?? WorkStatus.Created.ToString();
            var fileId = work?.FileId;

            if (existing.Status == IdempotencyStatus.Completed && !string.IsNullOrWhiteSpace(existing.ResponseJson))
                return new SubmissionStartResponse("Completed", existing.WorkId, ws, fileId, existing.ResponseJson);

            return new SubmissionStartResponse("InProgress", existing.WorkId, ws, fileId, null);
        }

        var workId = Guid.NewGuid();

        var workNew = new Work
        {
            Id = workId,
            StudentId = req.StudentId,
            StudentName = req.StudentName,
            AssignmentId = req.AssignmentId,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            Status = WorkStatus.Created
        };

        var subNew = new SubmissionIdempotency
        {
            IdempotencyKey = req.IdempotencyKey,
            RequestHash = req.RequestHash,
            Status = IdempotencyStatus.InProgress,
            WorkId = workId,
            ResponseJson = null,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.Works.Add(workNew);
        _db.Submissions.Add(subNew);

        try
        {
            await _db.SaveChangesAsync(ct);
            return new SubmissionStartResponse("InProgress", workId, workNew.Status.ToString(), workNew.FileId, null);
        }
        catch (DbUpdateException)
        {
            var again = await _db.Submissions.FirstOrDefaultAsync(x => x.IdempotencyKey == req.IdempotencyKey, ct);
            if (again is null) throw;

            if (!string.Equals(again.RequestHash, req.RequestHash, StringComparison.Ordinal))
                throw new InvalidOperationException("IDEMPOTENCY_KEY_CONFLICT");

            var work = await _db.Works.FirstOrDefaultAsync(w => w.Id == again.WorkId, ct);
            var ws = work?.Status.ToString() ?? WorkStatus.Created.ToString();
            var fileId = work?.FileId;

            if (again.Status == IdempotencyStatus.Completed && !string.IsNullOrWhiteSpace(again.ResponseJson))
                return new SubmissionStartResponse("Completed", again.WorkId, ws, fileId, again.ResponseJson);

            return new SubmissionStartResponse("InProgress", again.WorkId, ws, fileId, null);
        }
    }

    public async Task AttachFileAsync(Guid workId, AttachFileRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey)) throw new ArgumentException("IdempotencyKey required");
        if (string.IsNullOrWhiteSpace(req.FileId)) throw new ArgumentException("FileId required");

        var sub = await _db.Submissions.FirstOrDefaultAsync(x => x.IdempotencyKey == req.IdempotencyKey, ct);
        if (sub is null) throw new InvalidOperationException("IDEMPOTENCY_NOT_STARTED");
        if (sub.WorkId != workId) throw new InvalidOperationException("IDEMPOTENCY_WORK_MISMATCH");

        var work = await _db.Works.FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work is null) throw new InvalidOperationException("WORK_NOT_FOUND");

        if (!string.IsNullOrWhiteSpace(work.FileId) && !string.Equals(work.FileId, req.FileId, StringComparison.Ordinal))
            throw new InvalidOperationException("FILEID_CONFLICT");

        if (string.IsNullOrWhiteSpace(work.FileId))
        {
            work.FileId = req.FileId;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkUploadFailedAsync(Guid workId, CancellationToken ct)
    {
        var work = await _db.Works.FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work is null) return;

        work.Status = WorkStatus.FileUploadFailed;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SubmissionResult> AnalyzeAsync(Guid workId, AnalyzeRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey)) throw new ArgumentException("IdempotencyKey required");

        var sub = await _db.Submissions.FirstOrDefaultAsync(x => x.IdempotencyKey == req.IdempotencyKey, ct);
        if (sub is null) throw new InvalidOperationException("IDEMPOTENCY_NOT_STARTED");
        if (sub.WorkId != workId) throw new InvalidOperationException("IDEMPOTENCY_WORK_MISMATCH");

        if (sub.Status == IdempotencyStatus.Completed && !string.IsNullOrWhiteSpace(sub.ResponseJson))
        {
            var cached = JsonSerializer.Deserialize<SubmissionResult>(sub.ResponseJson, Json.Options);
            if (cached is not null) return cached;
        }

        var work = await _db.Works.FirstOrDefaultAsync(w => w.Id == workId, ct);
        if (work is null) throw new InvalidOperationException("WORK_NOT_FOUND");
        if (string.IsNullOrWhiteSpace(work.FileId)) throw new InvalidOperationException("FILE_NOT_ATTACHED");

        work.Status = WorkStatus.Analyzing;
        await _db.SaveChangesAsync(ct);

        try
        {
            var fileId = work.FileId!;

            string fileHash;
            await using (var stream = await _files.DownloadAsync(fileId, ct))
            {
                using var sha = SHA256.Create();
                var hashBytes = await sha.ComputeHashAsync(stream, ct);
                fileHash = Convert.ToHexString(hashBytes);
            }

            work.FileHashSha256 = fileHash;

            var source = await _db.Works
                .Where(w =>
                    w.AssignmentId == work.AssignmentId &&
                    w.FileHashSha256 == fileHash &&
                    w.SubmittedAtUtc < work.SubmittedAtUtc &&
                    w.StudentId != work.StudentId)
                .OrderBy(w => w.SubmittedAtUtc)
                .FirstOrDefaultAsync(ct);

            work.PlagiarismFlag = source is not null;
            work.PlagiarismSourceWorkId = source?.Id;

            await EnsurePlagiarismReportAsync(work, ct);

            if (_enableWordCloud)
            {
                await TryEnsureWordCloudReportAsync(work, ct);
            }

            work.Status = WorkStatus.Done;
            await _db.SaveChangesAsync(ct);

            var result = await BuildResultAsync(work.Id, ct);

            sub.Status = IdempotencyStatus.Completed;
            sub.ResponseJson = JsonSerializer.Serialize(result, Json.Options);
            sub.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Analyze failed for workId={WorkId}", workId);
            work.Status = WorkStatus.Failed;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task EnsurePlagiarismReportAsync(Work work, CancellationToken ct)
    {
        var existing = await _db.Reports.FirstOrDefaultAsync(r => r.WorkId == work.Id && r.Type == ReportType.Plagiarism, ct);
        if (existing is not null) return;

        var report = new Report
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            Type = ReportType.Plagiarism,
            Status = ReportStatus.Done,
            ResultJson = JsonSerializer.Serialize(new
            {
                plagiarism = work.PlagiarismFlag,
                sourceWorkId = work.PlagiarismSourceWorkId
            }, Json.Options),
            ArtifactFileId = null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.Reports.Add(report);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
        }
    }

    private async Task TryEnsureWordCloudReportAsync(Work work, CancellationToken ct)
    {
        var existing = await _db.Reports.FirstOrDefaultAsync(r => r.WorkId == work.Id && r.Type == ReportType.WordCloud, ct);
        if (existing is not null) return;

        try
        {
            await using var stream = await _files.DownloadAsync(work.FileId!, ct);
            var text = await TextTools.TryExtractUtf8TextAsync(stream, maxBytes: 512_000, ct);

            var freq = TextTools.BuildWordFreq(text, maxWords: 120);
            var expanded = TextTools.ExpandForWordCloud(freq, maxTotalTokens: 1500);

            if (string.IsNullOrWhiteSpace(expanded))
                return;

            var png = await _qc.BuildWordCloudPngAsync(expanded, 800, 500, ct);
            var artifactId = await _files.UploadRawAsync(png, "image/png", "wordcloud.png", ct);

            var report = new Report
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                Type = ReportType.WordCloud,
                Status = ReportStatus.Done,
                ResultJson = JsonSerializer.Serialize(new
                {
                    note = "WordCloud generated via QuickChart",
                    topWords = freq.OrderByDescending(x => x.Value).Take(20).ToArray()
                }, Json.Options),
                ArtifactFileId = artifactId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _db.Reports.Add(report);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "WordCloud generation failed (best-effort). workId={WorkId}", work.Id);

            var already = await _db.Reports.FirstOrDefaultAsync(r => r.WorkId == work.Id && r.Type == ReportType.WordCloud, ct);
            if (already is not null) return;

            var failed = new Report
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                Type = ReportType.WordCloud,
                Status = ReportStatus.Failed,
                ResultJson = JsonSerializer.Serialize(new
                {
                    error = "WordCloud failed",
                    message = ex.Message
                }, Json.Options),
                ArtifactFileId = null,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _db.Reports.Add(failed);
            try { await _db.SaveChangesAsync(ct); } catch { }
        }
    }

    private async Task<SubmissionResult> BuildResultAsync(Guid workId, CancellationToken ct)
    {
        var w = await _db.Works.FirstAsync(x => x.Id == workId, ct);

        var reps = await _db.Reports
            .Where(r => r.WorkId == workId)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        return new SubmissionResult(
            WorkId: w.Id,
            Status: w.Status.ToString(),
            Plagiarism: w.PlagiarismFlag,
            PlagiarismSourceWorkId: w.PlagiarismSourceWorkId,
            Reports: reps.Select(r => new ReportDto(
                ReportId: r.Id,
                Type: r.Type.ToString(),
                Status: r.Status.ToString(),
                ResultJson: r.ResultJson,
                ArtifactFileId: r.ArtifactFileId,
                CreatedAtUtc: r.CreatedAtUtc
            )).ToList()
        );
    }

    private static void ValidateStart(SubmissionStartRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.IdempotencyKey)) throw new ArgumentException("IdempotencyKey required");
        if (string.IsNullOrWhiteSpace(req.RequestHash)) throw new ArgumentException("RequestHash required");
        if (string.IsNullOrWhiteSpace(req.StudentId)) throw new ArgumentException("StudentId required");
        if (string.IsNullOrWhiteSpace(req.StudentName)) throw new ArgumentException("StudentName required");
        if (string.IsNullOrWhiteSpace(req.AssignmentId)) throw new ArgumentException("AssignmentId required");
    }
}
