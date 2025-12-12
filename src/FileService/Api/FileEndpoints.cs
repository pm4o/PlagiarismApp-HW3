using FileService.Storage;
using Microsoft.AspNetCore.Mvc;

namespace FileService.Api;

public static class FileEndpoints
{
    public sealed class UploadFileForm
    {
        [FromForm(Name = "file")]
        public IFormFile File { get; init; } = default!;
    }

    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { ok = true }));

        app.MapPost("/files", async (
                [FromForm] UploadFileForm form,
                LocalFileStore store,
                CancellationToken ct) =>
            {
                var file = form.File;

                if (file.Length <= 0)
                    return Results.BadRequest(new { error = "file is required (field name: file)" });

                try
                {
                    var meta = await store.SaveMultipartAsync(file, ct);
                    return Results.Ok(new
                    {
                        fileId = meta.FileId,
                        originalName = meta.OriginalName,
                        contentType = meta.ContentType,
                        size = meta.Size
                    });
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message, statusCode: 500);
                }
            })
            .WithMetadata(new ConsumesAttribute("multipart/form-data"))
            .DisableAntiforgery();

        app.MapPost("/files/raw", async (
            [FromQuery] string contentType,
            [FromQuery] string? originalName,
            HttpRequest request,
            LocalFileStore store,
            CancellationToken ct) =>
        {
            var name = string.IsNullOrWhiteSpace(originalName) ? "artifact.bin" : originalName;

            try
            {
                var meta = await store.SaveRawAsync(request.Body, contentType, name, ct);
                return Results.Ok(new
                {
                    fileId = meta.FileId,
                    originalName = meta.OriginalName,
                    contentType = meta.ContentType,
                    size = meta.Size
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        app.MapGet("/files/{fileId}", (string fileId, LocalFileStore store) =>
        {
            if (string.IsNullOrWhiteSpace(fileId))
                return Results.BadRequest(new { error = "fileId required" });

            try
            {
                var (binPath, meta) = store.GetForDownload(fileId);
                var contentType = meta?.ContentType ?? "application/octet-stream";
                var downloadName = meta?.OriginalName ?? fileId;
                return Results.File(binPath, contentType, downloadName);
            }
            catch
            {
                return Results.NotFound(new { error = "file not found" });
            }
        });

        app.MapGet("/files/{fileId}/meta", (string fileId, LocalFileStore store) =>
        {
            var meta = store.TryReadMeta(fileId);
            return meta is null ? Results.NotFound(new { error = "meta not found" }) : Results.Ok(meta);
        });
    }
}
