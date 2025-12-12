using System.Net;
using System.Text.Json;
using Gateway.Clients;
using Gateway.Support;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Api;

public static class GatewayEndpoints
{
    public sealed class CreateWorkForm
    {
        [FromForm(Name = "studentId")]
        public string StudentId { get; init; } = default!;

        [FromForm(Name = "studentName")]
        public string StudentName { get; init; } = default!;

        [FromForm(Name = "assignmentId")]
        public string AssignmentId { get; init; } = default!;

        [FromForm(Name = "file")]
        public IFormFile File { get; init; } = default!;
    }

    public static void MapGatewayEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        app.MapPost("/works", async (
                [FromHeader(Name = "Idempotency-Key")] string? idk,
                [FromForm] CreateWorkForm form,
                AnalysisApiClient analysis,
                FileApiClient files,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(idk))
                    return Results.BadRequest(new { error = "Idempotency-Key header is required" });

                var idemKey = idk.Trim();

                var studentId = form.StudentId;
                var studentName = form.StudentName;
                var assignmentId = form.AssignmentId;
                var file = form.File;

                if (string.IsNullOrWhiteSpace(studentId) ||
                    string.IsNullOrWhiteSpace(studentName) ||
                    string.IsNullOrWhiteSpace(assignmentId))
                    return Results.BadRequest(new { error = "studentId, studentName, assignmentId are required" });

                if (file.Length <= 0)
                    return Results.BadRequest(new { error = "file is required (field name: file)" });

                // temp + sha
                string tmpPath = "";
                string fileSha;
                try
                {
                    await using var input = file.OpenReadStream();
                    (fileSha, tmpPath) = await Hashing.SaveToTempAndHashAsync(input, ct);
                }
                catch (Exception ex)
                {
                    Hashing.TryDelete(tmpPath);
                    return Results.Problem($"Cannot read uploaded file: {ex.Message}", statusCode: 400);
                }

                var requestHash = Hashing.Sha256Hex($"{studentId}|{studentName}|{assignmentId}|{fileSha}");

                StartResponse? start;
                Guid workId;

                try
                {
                    using var startResp = await analysis.StartAsync(new
                    {
                        idempotencyKey = idemKey,
                        requestHash,
                        studentId,
                        studentName,
                        assignmentId
                    }, ct);

                    if (startResp.StatusCode == HttpStatusCode.Conflict)
                    {
                        Hashing.TryDelete(tmpPath);
                        return Results.Conflict(new
                        {
                            error = "Idempotency-Key conflict (same key used with different payload)"
                        });
                    }

                    if (!startResp.IsSuccessStatusCode)
                    {
                        var b = await HttpErrors.ReadBodySafeAsync(startResp, ct);
                        Hashing.TryDelete(tmpPath);
                        return HttpErrors.AsBadGateway($"Start failed: {b}", startResp.StatusCode);
                    }

                    var startJson = await startResp.Content.ReadAsStringAsync(ct);
                    start = JsonSerializer.Deserialize<StartResponse>(startJson, JsonOptions);

                    if (start is null)
                    {
                        Hashing.TryDelete(tmpPath);
                        return HttpErrors.AsBadGateway("Invalid start response from AnalysisService", startResp.StatusCode);
                    }

                    if (string.Equals(start.Kind, "Completed", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(start.ResponseJson))
                    {
                        Hashing.TryDelete(tmpPath);
                        return Results.Content(start.ResponseJson!, "application/json");
                    }

                    workId = start.WorkId;
                }
                catch (Exception ex)
                {
                    Hashing.TryDelete(tmpPath);
                    return HttpErrors.AsServiceUnavailable($"AnalysisService unavailable: {ex.Message}");
                }

                if (string.IsNullOrWhiteSpace(start!.ExistingFileId))
                {
                    try
                    {
                        using var uploadResp = await files.UploadAsync(tmpPath, file.FileName, ct);

                        if (!uploadResp.IsSuccessStatusCode)
                        {
                            try { await analysis.UploadFailedAsync(workId, ct); } catch { }
                            var b = await HttpErrors.ReadBodySafeAsync(uploadResp, ct);
                            return HttpErrors.AsBadGateway($"File upload failed: {b}", uploadResp.StatusCode);
                        }

                        var uploadDto = await files.ReadUploadResponseAsync(uploadResp, ct);
                        if (uploadDto is null || string.IsNullOrWhiteSpace(uploadDto.FileId))
                        {
                            try { await analysis.UploadFailedAsync(workId, ct); } catch { }
                            return HttpErrors.AsBadGateway("Invalid upload response from FileService", uploadResp.StatusCode);
                        }

                        var fileId = uploadDto.FileId!;

                        try
                        {
                            using var attachResp = await analysis.AttachFileAsync(workId, new
                            {
                                idempotencyKey = idemKey,
                                fileId
                            }, ct);

                            if (attachResp.StatusCode == HttpStatusCode.Conflict)
                                return Results.Conflict(new
                                {
                                    error = "FileId conflict: different file already attached to this work"
                                });

                            if (!attachResp.IsSuccessStatusCode)
                            {
                                var b = await HttpErrors.ReadBodySafeAsync(attachResp, ct);
                                return HttpErrors.AsBadGateway($"Attach-file failed: {b}", attachResp.StatusCode);
                            }
                        }
                        catch (Exception ex)
                        {
                            return HttpErrors.AsServiceUnavailable($"AnalysisService unavailable during attach-file: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        try { await analysis.UploadFailedAsync(workId, ct); } catch { }
                        return HttpErrors.AsServiceUnavailable($"FileService unavailable: {ex.Message}");
                    }
                    finally
                    {
                        Hashing.TryDelete(tmpPath);
                    }
                }
                else
                {
                    Hashing.TryDelete(tmpPath);
                }

                try
                {
                    using var analyzeResp = await analysis.AnalyzeAsync(workId, new { idempotencyKey = idemKey }, ct);
                    var analyzeBody = await HttpErrors.ReadBodySafeAsync(analyzeResp, ct);

                    if (!analyzeResp.IsSuccessStatusCode)
                        return HttpErrors.AsBadGateway($"Analyze failed: {analyzeBody}", analyzeResp.StatusCode);

                    return Results.Content(analyzeBody, "application/json");
                }
                catch (Exception ex)
                {
                    return HttpErrors.AsServiceUnavailable($"AnalysisService unavailable during analyze: {ex.Message}");
                }
            })
            .WithMetadata(new ConsumesAttribute("multipart/form-data")) // важно для swagger
            .DisableAntiforgery();

        app.MapGet("/works/{workId:guid}", async (Guid workId, AnalysisApiClient analysis, CancellationToken ct) =>
        {
            try
            {
                using var resp = await analysis.GetWorkAsync(workId, ct);
                var body = await HttpErrors.ReadBodySafeAsync(resp, ct);
                return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                return HttpErrors.AsServiceUnavailable($"AnalysisService unavailable: {ex.Message}");
            }
        });

        app.MapGet("/works/{workId:guid}/reports", async (Guid workId, AnalysisApiClient analysis, CancellationToken ct) =>
        {
            try
            {
                using var resp = await analysis.GetReportsAsync(workId, ct);
                var body = await HttpErrors.ReadBodySafeAsync(resp, ct);
                return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                return HttpErrors.AsServiceUnavailable($"AnalysisService unavailable: {ex.Message}");
            }
        });

        app.MapGet("/files/{fileId}", async (string fileId, FileApiClient files, CancellationToken ct) =>
        {
            try
            {
                using var resp = await files.DownloadAsync(fileId, ct);

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await HttpErrors.ReadBodySafeAsync(resp, ct);
                    return Results.Content(body, "application/json", statusCode: (int)resp.StatusCode);
                }

                var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

                await using var s = await resp.Content.ReadAsStreamAsync(ct);
                var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                ms.Position = 0;

                return Results.File(ms, contentType);
            }
            catch (Exception ex)
            {
                return HttpErrors.AsServiceUnavailable($"FileService unavailable: {ex.Message}");
            }
        });
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class StartResponse
    {
        public string Kind { get; set; } = default!;
        public Guid WorkId { get; set; }
        public string WorkStatus { get; set; } = default!;
        public string? ExistingFileId { get; set; }
        public string? ResponseJson { get; set; }
    }
}
