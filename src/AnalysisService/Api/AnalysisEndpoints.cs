using AnalysisService.Application;
using AnalysisService.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnalysisService.Api;

public static class AnalysisEndpoints
{
    public sealed class UploadWorkFileForm
    {
        [FromForm(Name = "file")]
        public IFormFile File { get; init; } = default!;
    }

    public static void MapAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));
        
        app.MapPost("/internal/works/{workId:guid}/upload-file", async (
                Guid workId,
                [FromForm] UploadWorkFileForm form,
                SubmissionAppService svc,
                CancellationToken ct) =>
            {
                var file = form.File;

                if (file.Length <= 0)
                    return Results.BadRequest(new { error = "file is required (field name: file)" });

                try
                {
                    return Results.Ok(new
                    {
                        workId,
                        originalName = file.FileName,
                        contentType = file.ContentType,
                        size = file.Length
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: 500);
                }
            })
            .WithMetadata(new ConsumesAttribute("multipart/form-data"))
            .DisableAntiforgery();
        
        app.MapPost("/internal/submissions/start", async (SubmissionStartRequest req, SubmissionAppService svc, CancellationToken ct) =>
        {
            try
            {
                var res = await svc.StartAsync(req, ct);
                return Results.Ok(res);
            }
            catch (InvalidOperationException ex) when (ex.Message == "IDEMPOTENCY_KEY_CONFLICT")
            {
                return Results.Conflict(new { error = "Idempotency-Key already used with different request payload" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/internal/works/{workId:guid}/attach-file", async (Guid workId, AttachFileRequest req, SubmissionAppService svc, CancellationToken ct) =>
        {
            try
            {
                await svc.AttachFileAsync(workId, req, ct);
                return Results.Ok(new { ok = true });
            }
            catch (InvalidOperationException ex) when (ex.Message == "FILEID_CONFLICT")
            {
                return Results.Conflict(new { error = "Different fileId already attached to this work" });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/internal/works/{workId:guid}/upload-failed", async (Guid workId, SubmissionAppService svc, CancellationToken ct) =>
        {
            await svc.MarkUploadFailedAsync(workId, ct);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/internal/works/{workId:guid}/analyze", async (Guid workId, AnalyzeRequest req, SubmissionAppService svc, CancellationToken ct) =>
        {
            try
            {
                var res = await svc.AnalyzeAsync(workId, req, ct);
                return Results.Ok(res);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/works/{workId:guid}", async (Guid workId, AppDbContext db, CancellationToken ct) =>
        {
            var w = await db.Works.FirstOrDefaultAsync(x => x.Id == workId, ct);
            return w is null ? Results.NotFound(new { error = "work not found" }) : Results.Ok(w);
        });

        app.MapGet("/works/{workId:guid}/reports", async (Guid workId, AppDbContext db, CancellationToken ct) =>
        {
            var reps = await db.Reports
                .Where(r => r.WorkId == workId)
                .OrderBy(r => r.CreatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(reps);
        });
    }
}
