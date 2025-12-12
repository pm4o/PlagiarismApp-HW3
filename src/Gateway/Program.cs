using Gateway.Api;
using Gateway.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Gateway", Version = "v1" });
    c.OperationFilter<Gateway.Api.SwaggerFileUploadOperationFilter>();
});


var analysisUrl = Environment.GetEnvironmentVariable("ANALYSIS_URL") ?? "http://localhost:8082";
var fileUrl = Environment.GetEnvironmentVariable("FILE_SERVICE_URL") ?? "http://localhost:8081";

var timeoutVar = Environment.GetEnvironmentVariable("GATEWAY_TIMEOUT_SECONDS");
var timeout = TimeSpan.FromSeconds(int.TryParse(timeoutVar, out var s) ? s : 25);

builder.Services.AddHttpClient<AnalysisApiClient>(http =>
{
    http.BaseAddress = new Uri(analysisUrl);
    http.Timeout = timeout;
});

builder.Services.AddHttpClient<FileApiClient>(http =>
{
    http.BaseAddress = new Uri(fileUrl);
    http.Timeout = timeout;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGatewayEndpoints();

app.Run();