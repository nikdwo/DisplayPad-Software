using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Execution;
using DisplayPad.Shared.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DisplayPad.Agent;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);

        WebApplication? app = null;
        AgentConfig? config = null;
        try
        {
            config = AgentConfig.LoadOrCreate();
            var agentCertificate = AgentCertificateStore.LoadOrCreate(config);
            app = AgentWebApplication.Build(config, agentCertificate.Certificate);
            app.StartAsync().GetAwaiter().GetResult();
            Log.Info($"Agent gestartet: HTTPS-Port {config.Port}, Zertifikat {agentCertificate.Fingerprint}");
            Application.Run(new TrayAppContext(config, agentCertificate.Fingerprint, app));
        }
        catch (Exception ex)
        {
            Log.Error($"Agent-Start fehlgeschlagen: {ex.Message}");
            var language = config?.Language ?? (ex as AgentConfigException)?.Language ?? "de";
            var message = ex is AgentConfigException configError
                ? AgentText.Get(language, configError.TextKey)
                : string.Format(AgentText.Get(language, "ErrorWithTechnicalDetail"),
                    AgentText.Get(language, "StartFailed"), ex.Message);
            MessageBox.Show(message, AgentText.Get(language, "StartFailedTitle"),
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (app is not null)
            {
                try
                {
                    using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    app.StopAsync(stopTimeout.Token).GetAwaiter().GetResult();
                }
                catch { }
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }
}

public static class AgentWebApplication
{
    public const long MaximumBodyBytes = 64 * 1024;

    public static WebApplication Build(AgentConfig config, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate)
    {
        var address = IPAddress.Parse(config.BindAddress);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = MaximumBodyBytes;
            options.Listen(address, config.Port, listen =>
                listen.UseHttps(https => https.ServerCertificate = certificate));
        });
        var app = builder.Build();
        var failures = new FailedAuthenticationLimiter(5, TimeSpan.FromMinutes(1));

        app.Use(async (context, next) =>
        {
            var source = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (failures.IsBlocked(source))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentTooManyAuthenticationFailures,
                    Error = AgentText.Get(config.Language, "TooManyAuth")
                });
                return;
            }

            var supplied = context.Request.Headers["X-Auth-Token"].ToString();
            if (!TokensEqual(supplied, config.Token))
            {
                failures.RecordFailure(source);
                Log.Error($"Anfrage von {source} wegen ungültiger Anmeldung abgelehnt.");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentInvalidAuthentication,
                    Error = AgentText.Get(config.Language, "InvalidAuth")
                });
                return;
            }

            failures.RecordSuccess(source);
            await next();
        });

        app.MapGet("/ping", () => Results.Ok(new PingResponse
        {
            MachineName = Environment.MachineName,
            Version = ProductVersion.FromAssembly(typeof(Program).Assembly)
        }));

        app.MapPost("/execute", (ExecuteRequest request) =>
        {
            if (request.Action is null || request.Action.Type is not (KeyActionType.Hotkey or KeyActionType.Command))
                return Results.Json(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentUnsupportedAction,
                    Error = AgentText.Get(config.Language, "UnsupportedAction")
                }, statusCode: 400);
            if (request.Action.Type == KeyActionType.Hotkey && string.IsNullOrWhiteSpace(request.Action.Hotkey))
                return Results.Json(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentMissingHotkey,
                    Error = AgentText.Get(config.Language, "MissingHotkey")
                }, statusCode: 400);
            if (request.Action.Type == KeyActionType.Command && string.IsNullOrWhiteSpace(request.Action.CommandLine))
                return Results.Json(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentMissingCommand,
                    Error = AgentText.Get(config.Language, "MissingCommand")
                }, statusCode: 400);

            try
            {
                if (request.Action.Type == KeyActionType.Hotkey)
                {
                    Log.Info("Remote-Hotkey wird ausgeführt.");
                    HotkeyExecutor.Send(request.Action.Hotkey!);
                }
                else
                {
                    Log.Info("Remote-Befehl wird ausgeführt (Inhalt redigiert).");
                    CommandExecutor.Run(request.Action.CommandLine!, request.Action.WorkingDirectory);
                }
                return Results.Ok(new ExecuteResponse { Success = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Remote-Ausführung fehlgeschlagen: {ex.GetType().Name}");
                return Results.Json(new ExecuteResponse
                {
                    Success = false,
                    ErrorCode = OperationErrorCode.AgentExecutionFailed,
                    Error = AgentText.Get(config.Language, "ExecutionFailed")
                }, statusCode: 500);
            }
        });

        return app;
    }

    private static bool TokensEqual(string supplied, string expected)
    {
        var suppliedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var expectedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes) && supplied.Length == expected.Length;
    }
}

public sealed class FailedAuthenticationLimiter
{
    private readonly int _maximumFailures;
    private readonly TimeSpan _window;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _failures = new();
    private readonly object _sync = new();

    public FailedAuthenticationLimiter(int maximumFailures, TimeSpan window)
    {
        _maximumFailures = maximumFailures;
        _window = window;
    }

    public bool IsBlocked(string source)
    {
        lock (_sync)
        {
            var queue = GetCurrentFailures(source);
            return queue.Count >= _maximumFailures;
        }
    }

    public void RecordFailure(string source)
    {
        lock (_sync)
            GetCurrentFailures(source).Enqueue(DateTimeOffset.UtcNow);
    }

    public void RecordSuccess(string source)
    {
        lock (_sync)
            _failures.Remove(source);
    }

    private Queue<DateTimeOffset> GetCurrentFailures(string source)
    {
        if (!_failures.TryGetValue(source, out var queue))
            _failures[source] = queue = new Queue<DateTimeOffset>();
        var cutoff = DateTimeOffset.UtcNow - _window;
        while (queue.TryPeek(out var failure) && failure < cutoff)
            queue.Dequeue();
        return queue;
    }
}

internal sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly AgentConfig _config;
    private readonly WebApplication _app;
    private readonly string _fingerprint;

    public TrayAppContext(AgentConfig config, string fingerprint, WebApplication app)
    {
        _config = config;
        _fingerprint = fingerprint;
        _app = app;

        var menu = new ContextMenuStrip();
        menu.Items.Add($"DisplayPad Agent – HTTPS {config.Port}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(AgentText.Get(config.Language, "CopyPairing"), null, (_, _) => CopyPairingData());
        menu.Items.Add(AgentText.Get(config.Language, "RotateToken"), null, (_, _) => RotateToken());
        menu.Items.Add(AgentText.Get(config.Language, "OpenLog"), null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(AgentText.Get(config.Language, "Exit"), null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = $"DisplayPad Agent (HTTPS {config.Port})",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.ShowBalloonTip(3000, "DisplayPad Agent",
            string.Format(AgentText.Get(config.Language, "Running"), Environment.MachineName, config.Port), ToolTipIcon.Info);
    }

    private void CopyPairingData()
    {
        Clipboard.SetText(
            $"{AgentText.Get(_config.Language, "PairingAddress")}: {Environment.MachineName}:{_config.Port}{Environment.NewLine}" +
            $"{AgentText.Get(_config.Language, "PairingToken")}: {_config.Token}{Environment.NewLine}" +
            $"{AgentText.Get(_config.Language, "PairingFingerprint")}: {_fingerprint}");
        _trayIcon.ShowBalloonTip(2000, "DisplayPad Agent", AgentText.Get(_config.Language, "PairingCopied"), ToolTipIcon.Info);
    }

    private void RotateToken()
    {
        AgentConfig.RotateToken(_config);
        Clipboard.SetText(_config.Token);
        _trayIcon.ShowBalloonTip(3000, "DisplayPad Agent", AgentText.Get(_config.Language, "TokenRotated"), ToolTipIcon.Warning);
    }

    private static void OpenLog()
    {
        if (File.Exists(Log.LogPath))
            Process.Start(new ProcessStartInfo(Log.LogPath) { UseShellExecute = true });
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        try
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _app.StopAsync(stopTimeout.Token).GetAwaiter().GetResult();
        }
        catch { }
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _trayIcon.Dispose();
        base.Dispose(disposing);
    }
}
