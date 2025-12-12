using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AnalysisService.Api;

public sealed class SwaggerWorkUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var relativePath = context.ApiDescription.RelativePath?.Trim('/');
        var method = context.ApiDescription.HttpMethod;

        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
            return;

        if (relativePath is null ||
            !relativePath.Equals("internal/works/{workId}/upload-file", StringComparison.OrdinalIgnoreCase))
            return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Required = new HashSet<string> { "file" },
                        Properties = new Dictionary<string, OpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema { Type = "string", Format = "binary" }
                        }
                    }
                }
            }
        };
    }
}