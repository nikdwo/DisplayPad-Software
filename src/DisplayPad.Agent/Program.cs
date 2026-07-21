using System.Diagnostics;
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
        var config = AgentConfig.LoadOrCreate();
        Log.Info($"Agent startet auf http://{config.BindAddress}:{config.Port}");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{config.BindAddress}:{config.Port}");
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            var token = context.Request.Headers["X-Auth-Token"].ToString();
            if (token != config.Token)
            {
                Log.Error($"Abgelehnte Anfrage von {context.Connection.RemoteIpAddress} (ungültiges Token)");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ExecuteResponse { Success = false, Error = "Ungültiges Token" });
                return;
            }
            await next();
        });

        app.MapGet("/ping", () => new PingResponse { MachineName = Environment.MachineName });

        app.MapPost("/execute", (ExecuteRequest request) =>
        {
            try
            {
                switch (request.Action.Type)
                {
                    case KeyActionType.Hotkey:
                        Log.Info($"Hotkey: {request.Action.Hotkey}");
                        HotkeyExecutor.Send(request.Action.Hotkey ?? "");
                        break;
                    case KeyActionType.Command:
                        Log.Info($"Befehl: {request.Action.CommandLine}");
                        CommandExecutor.Run(request.Action.CommandLine ?? "", request.Action.WorkingDirectory);
                        break;
                    default:
                        return Results.Ok(new ExecuteResponse { Success = false, Error = "Keine Aktion konfiguriert" });
                }
                return Results.Ok(new ExecuteResponse { Success = true });
            }
            catch (Exception ex)
            {
                Log.Error($"Ausführung fehlgeschlagen: {ex.Message}");
                return Results.Ok(new ExecuteResponse { Success = false, Error = ex.Message });
            }
        });

        _ = app.RunAsync();

        Application.EnableVisualStyles();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new TrayAppContext(config, () => app.StopAsync().Wait(TimeSpan.FromSeconds(3))));
    }
}

internal class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Action _shutdown;

    public TrayAppContext(AgentConfig config, Action shutdown)
    {
        _shutdown = shutdown;

        var menu = new ContextMenuStrip();
        menu.Items.Add($"DisplayPad Agent – Port {config.Port}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Token in Zwischenablage kopieren", null, (_, _) =>
        {
            Clipboard.SetText(config.Token);
            _trayIcon!.ShowBalloonTip(2000, "DisplayPad Agent", "Token kopiert.", ToolTipIcon.Info);
        });
        menu.Items.Add("Token anzeigen", null, (_, _) =>
            MessageBox.Show($"Token: {config.Token}\n\nDiesen Token in der Host-App eintragen.",
                "DisplayPad Agent", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("Log öffnen", null, (_, _) =>
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "agent.log");
            if (File.Exists(logPath))
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = $"DisplayPad Agent (Port {config.Port})",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.ShowBalloonTip(3000, "DisplayPad Agent",
            $"Läuft auf Port {config.Port} – {Environment.MachineName}", ToolTipIcon.Info);
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        try { _shutdown(); } catch { /* Kestrel-Stop darf das Beenden nicht blockieren */ }
        Application.Exit();
    }
}
