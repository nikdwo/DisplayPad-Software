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
    private readonly Form _window;
    private readonly TextBox _tokenBox;
    private readonly AgentConfig _config;
    private readonly WebApplication _app;
    private readonly string _fingerprint;
    private bool _isExiting;

    public TrayAppContext(AgentConfig config, string fingerprint, WebApplication app)
    {
        _config = config;
        _fingerprint = fingerprint;
        _app = app;

        (_window, _tokenBox) = CreateWindow();

        var menu = new ContextMenuStrip();
        menu.Items.Add($"DisplayPad Agent – HTTPS {config.Port}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(AgentText.Get(config.Language, "ShowWindow"), null, (_, _) => ShowWindow());
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
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
        _trayIcon.ShowBalloonTip(3000, "DisplayPad Agent",
            string.Format(AgentText.Get(config.Language, "Running"), Environment.MachineName, config.Port), ToolTipIcon.Info);
        ShowWindow();
    }

    private (Form Window, TextBox TokenBox) CreateWindow()
    {
        var window = new Form
        {
            Text = AgentText.Get(_config.Language, "WindowTitle"),
            Icon = System.Drawing.SystemIcons.Application,
            StartPosition = FormStartPosition.CenterScreen,
            MinimumSize = new System.Drawing.Size(620, 220),
            ClientSize = new System.Drawing.Size(720, 220),
            MaximizeBox = false
        };
        window.FormClosing += (_, eventArgs) =>
        {
            if (_isExiting) return;
            eventArgs.Cancel = true;
            window.Hide();
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        TextBox AddField(int row, string textKey, string value)
        {
            var label = AgentText.Get(_config.Language, textKey);
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 12, 6)
            }, 0, row);
            var field = new TextBox
            {
                Text = value,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                AccessibleName = label,
                Margin = new Padding(0, 3, 0, 3)
            };
            layout.Controls.Add(field, 1, row);
            return field;
        }

        AddField(0, "PairingAddress", $"{Environment.MachineName}:{_config.Port}");
        var tokenBox = AddField(1, "PairingToken", _config.Token);
        AddField(2, "PairingFingerprint", _fingerprint);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.Add(new Button
        {
            Text = AgentText.Get(_config.Language, "CopyPairing"),
            AutoSize = true
        });
        buttons.Controls[^1].Click += (_, _) => CopyPairingData();
        buttons.Controls.Add(new Button
        {
            Text = AgentText.Get(_config.Language, "RotateToken"),
            AutoSize = true
        });
        buttons.Controls[^1].Click += (_, _) => RotateToken();
        buttons.Controls.Add(new Button
        {
            Text = AgentText.Get(_config.Language, "OpenLog"),
            AutoSize = true
        });
        buttons.Controls[^1].Click += (_, _) => OpenLog();
        buttons.Controls.Add(new Button
        {
            Text = AgentText.Get(_config.Language, "HideWindow"),
            AutoSize = true
        });
        buttons.Controls[^1].Click += (_, _) => window.Hide();
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        window.Controls.Add(layout);
        return (window, tokenBox);
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized)
            _window.WindowState = FormWindowState.Normal;
        _window.Activate();
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
        _tokenBox.Text = _config.Token;
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
        _isExiting = true;
        _window.Close();
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
        if (disposing)
        {
            _window.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
