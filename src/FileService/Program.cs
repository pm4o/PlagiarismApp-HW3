using FileService.Api;
using FileService.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FileService", Version = "v1" });
    c.OperationFilter<FileService.Api.SwaggerFileUploadOperationFilter>();
});


var storagePath = Environment.GetEnvironmentVariable("STORAGE_PATH")
                  ?? Path.Combine(AppContext.BaseDirectory, "data");

builder.Services.AddSingleton(new LocalFileStore(storagePath));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapFileEndpoints();

app.Run();