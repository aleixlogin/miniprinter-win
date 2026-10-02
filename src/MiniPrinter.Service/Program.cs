using System.Net;
using MiniPrinter.Control;
using MiniPrinter.Service;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "MiniPrinter");

var paths = new ServicePaths(builder.Configuration["DataDirectory"]);
var controlPort = builder.Configuration.GetValue("ControlPort", ControlDefaults.Port);

builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, controlPort));
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<PrinterManager>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobQueue>());
builder.Services.AddSingleton<IppHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<IppHost>());
builder.Services.AddSingleton<StatusBuilder>();
builder.Services.AddSingleton<BatteryMonitor>();
builder.Services.AddSingleton(MiniPrinter.Imaging.TemplateCatalog.ForDataDirectory(paths.DataDirectory));
builder.Services.AddSingleton<PrintRequests>();
builder.Services.AddSingleton<AutomationToken>();
builder.Services.AddSingleton(sp => new TelemetryLog(paths, sp.GetRequiredService<ILogger<TelemetryLog>>(), sp.GetRequiredService<PrinterManager>()));

var app = builder.Build();
var token = ControlApi.EnsureToken(paths, app.Logger);
Directory.CreateDirectory(Path.Combine(paths.DataDirectory, "templates")); // user templates: read for everyone (inherited ACL), write for admins and the service
app.Services.GetRequiredService<TelemetryLog>();      // start recording status replies
var battery = app.Services.GetRequiredService<BatteryMonitor>();
app.Services.GetRequiredService<JobQueue>().LowBatteryProbe = () => battery.IsLow;
ControlApi.Map(app, token);
app.Logger.LogInformation("MiniPrinter control API on http://127.0.0.1:{Port}/api (data: {Data})", controlPort, paths.DataDirectory);
app.Run();

public partial class Program;
