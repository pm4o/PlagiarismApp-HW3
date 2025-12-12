using AnalysisService.Api;
using AnalysisService.Application;
using AnalysisService.Data;
using AnalysisService.Infra;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AnalysisService", Version = "v1" });
    c.OperationFilter<AnalysisService.Api.SwaggerWorkUploadOperationFilter>();
});

var pg = builder.Configuration.GetConnectionString("pg")
         ?? Environment.GetEnvironmentVariable("PG_CONNECTION")
         ?? "Host=localhost;Port=5432;Database=hw3;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(pg));

builder.Services.AddHttpClient<FileClient>(http =>
{
    http.BaseAddress = new Uri(Environment.GetEnvironmentVariable("FILE_SERVICE_URL") ?? "http://localhost:8081");
    http.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient<QuickChartClient>(http =>
{
    http.BaseAddress = new Uri("https://quickchart.io");
    var t = Environment.GetEnvironmentVariable("QUICKCHART_TIMEOUT_SECONDS");
    http.Timeout = TimeSpan.FromSeconds(int.TryParse(t, out var s) ? s : 15);
});

builder.Services.AddScoped<SubmissionAppService>(sp =>
{
    var enable = string.Equals(Environment.GetEnvironmentVariable("ENABLE_WORDCLOUD"), "true", StringComparison.OrdinalIgnoreCase);
    return new SubmissionAppService(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<FileClient>(),
        sp.GetRequiredService<QuickChartClient>(),
        sp.GetRequiredService<ILogger<SubmissionAppService>>(),
        enable
    );
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.UseSwagger();
app.UseSwaggerUI();

app.MapAnalysisEndpoints();

app.Run();