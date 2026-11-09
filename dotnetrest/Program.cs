using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateSlimBuilder(args);

// /app/config-Overlay (K8s-Volume) rangiert ueber den image-gebackenen appsettings.json,
// aber unter ENV-Variablen und Kommandozeilen-Argumenten -> beide erneut anhaengen,
// damit die Default-Reihenfolge (JSON < ENV < Args) erhalten bleibt.
// reloadOnChange: false, weil Kubernetes ConfigMap-Updates per Tausch des ..data-Symlinks
// ausrollt, den der FileSystemWatcher meist nicht sieht; Kestrel-Limits werden ohnehin nur
// beim Start gelesen. Aenderungen an der ConfigMap -> kubectl rollout restart.
builder.Configuration
    .AddJsonFile("/app/config/appsettings.Production.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// CreateSlimBuilder registriert intern AddSimpleConsole(), das FormatterName fest auf "simple"
// setzt und Logging:Console:FormatterName aus der Konfiguration damit ueberschreibt.
// Ausserhalb von Development deshalb explizit den JSON-Formatter aktivieren (wird zuletzt
// konfiguriert und gewinnt).
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole();
}

// Laesst laufenden Requests bei SIGTERM bis zu 30s Zeit, bevor der Host hart beendet wird
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddOpenApi();
builder.Services.AddSingleton<TodoService>();
builder.Services.AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck("ready", () => HealthCheckResult.Healthy(), tags: ["ready"]);

// Liefert für unhandled Exceptions einen RFC-7807-konformen ProblemDetails-Body
// statt eines nackten 500 ohne Inhalt
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapTodoEndpoints();

app.Run();
