using System.Security.Cryptography;
using System.Text;
using Haven.BuildAgent;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BuildAgentOptions>(
    builder.Configuration.GetSection(BuildAgentOptions.SectionName));
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<BuildJobService>();
builder.Services.AddSingleton<AppProcessService>();
builder.Services.AddSingleton<WindowCaptureService>();
builder.Services.AddSingleton<ImageComparisonService>();
builder.Services.AddHttpClient<VisualReviewService>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com");
    client.Timeout = TimeSpan.FromMinutes(3);
});

var app = builder.Build();
BuildAgentOptions options = app.Services.GetRequiredService<IOptions<BuildAgentOptions>>().Value;
options.Validate();

string apiKey = Environment.GetEnvironmentVariable("HAVEN_BUILD_AGENT_KEY")
    ?? throw new InvalidOperationException(
        "HAVEN_BUILD_AGENT_KEY is not set. Refusing to start an unauthenticated build agent.");
byte[] expectedApiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));

app.Use(async (HttpContext context, RequestDelegate next) =>
{
    try
    {
        await next(context).ConfigureAwait(false);
    }
    catch (Exception exception)
    {
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Haven.BuildAgent.Request");
        logger.LogError(exception, "Request {Method} {Path} failed.", context.Request.Method, context.Request.Path);

        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.StatusCode = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            FileNotFoundException => StatusCodes.Status404NotFound,
            DirectoryNotFoundException => StatusCodes.Status404NotFound,
            TimeoutException => StatusCodes.Status504GatewayTimeout,
            InvalidOperationException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        await context.Response.WriteAsJsonAsync(new
        {
            error = exception.Message,
            type = exception.GetType().Name
        }).ConfigureAwait(false);
    }
});

app.Use(async (HttpContext context, RequestDelegate next) =>
{
    string suppliedApiKey = context.Request.Headers["X-Haven-Agent-Key"].ToString();
    byte[] suppliedApiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedApiKey));
    if (!CryptographicOperations.FixedTimeEquals(expectedApiKeyHash, suppliedApiKeyHash))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid build-agent API key." }).ConfigureAwait(false);
        return;
    }

    await next(context).ConfigureAwait(false);
});

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(options.ArtifactRootPath),
    RequestPath = "/artifacts",
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    repository = options.RepositoryRootPath,
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/profiles", () => Results.Ok(new
{
    buildProfiles = options.BuildProfiles.Keys.Order(StringComparer.OrdinalIgnoreCase),
    runProfiles = options.RunProfiles.Keys.Order(StringComparer.OrdinalIgnoreCase),
    referenceImages = options.ReferenceImages.Keys.Order(StringComparer.OrdinalIgnoreCase),
    aiVisualReviewConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
        && (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_MODEL"))
            || !string.IsNullOrWhiteSpace(options.VisualModel))
}));

app.MapPost("/api/jobs/build", (BuildRequest request, BuildJobService jobs) =>
{
    JobSnapshot job = jobs.StartBuild(request);
    return Results.Accepted($"/api/jobs/{job.Id}", job);
});

app.MapPost("/api/jobs/test", (TestRequest request, BuildJobService jobs) =>
{
    JobSnapshot job = jobs.StartTest(request);
    return Results.Accepted($"/api/jobs/{job.Id}", job);
});

app.MapGet("/api/jobs/{jobId:guid}", (Guid jobId, BuildJobService jobs) =>
    Results.Ok(jobs.Get(jobId)));

app.MapPost("/api/runs/start", (StartRunRequest request, AppProcessService runs) =>
    Results.Ok(runs.Start(request)));

app.MapGet("/api/runs", (AppProcessService runs) => Results.Ok(runs.List()));

app.MapGet("/api/runs/{runId:guid}", (Guid runId, AppProcessService runs) =>
    Results.Ok(runs.Get(runId)));

app.MapPost("/api/runs/{runId:guid}/stop", (Guid runId, AppProcessService runs) =>
    Results.Ok(runs.Stop(runId)));

app.MapPost("/api/captures", async (
    CaptureRequest request,
    WindowCaptureService capture,
    HttpContext context) =>
{
    CaptureResult result = await capture.CaptureAsync(request, context.RequestAborted).ConfigureAwait(false);
    return Results.Ok(result);
});

app.MapPost("/api/visual/compare", async (
    VisualCompareRequest request,
    VisualReviewService visualReview,
    HttpContext context) =>
{
    VisualComparisonResult result = await visualReview
        .CompareAsync(request, context.RequestAborted)
        .ConfigureAwait(false);
    return Results.Ok(result);
});

app.Run();
