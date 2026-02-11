using Microsoft.AspNetCore.Mvc;
using AlabamaWalkabilityApi.Models;
using AlabamaWalkabilityApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Allow large CSV uploads (e.g. national EPA walkability ~500MB+)
builder.WebHost.ConfigureKestrel(opts => opts.Limits.MaxRequestBodySize = 524_288_000); // 500 MB

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
// builder.Services.AddEndpointsApiExplorer();
// builder.Services.AddSwaggerGen(); // add Swashbuckle.AspNetCore package
builder.Services.AddScoped<IWalkabilityService, WalkabilityService>();
builder.Services.AddScoped<IWalkabilityImportService, WalkabilityImportService>();
builder.Services.AddScoped<WalkabilityDbContext>();
builder.Services.AddHttpClient<IDataGovService, DataGovService>()
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(15));
builder.Services.AddHostedService<ScheduledImportService>();

// Multipart form limit (separate from Kestrel MaxRequestBodySize)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opts =>
{
    opts.MultipartBodyLengthLimit = 524_288_000; // 500 MB
});

builder.Services.Configure<ApiBehaviorOptions>(opts =>
{
    opts.InvalidModelStateResponseFactory = ctx =>
    {
        var details = ctx.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
        return new BadRequestObjectResult(new ApiError("Validation failed", "VALIDATION_ERROR", details));
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.MapControllers();

// No-DB health check so you can confirm the server is up
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", now = DateTime.UtcNow }));

app.Run();
