using System.IO;
using System.Linq;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Joktan.Certificates;
using Joktan.Core;
using Joktan.Help.Views;
using Joktan.Mqtt;
using Joktan.Notes;
using Joktan.Passwords;
using Joktan.Settings;
using Joktan.Settings.Themes;
using Joktan.Dashboard.Telemetry;
using Joktan.TopicDashboards;
using Joktan.ViewModels;
using Joktan.Views;

namespace Joktan;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Before the first window, so the splash is already in the chosen theme.
        var themes = ThemeEditor.Load();
        themes.Apply();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tool windows such as the topic explorer must not keep the app alive on their own.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            Dispatcher.UIThread.Post(() => _ = StartAsync(desktop, splash, themes), DispatcherPriority.Background);
        }

        MacDockIcon.Apply();
        base.OnFrameworkInitializationCompleted();
    }

    // How long the splash stays at least, so it does not just flash when start-up is quick.
    private static readonly TimeSpan MinimumSplashTime = TimeSpan.FromSeconds(1.5);

    /// <summary>Loads everything behind the splash, then swaps it for the main window.</summary>
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, ThemeEditor themes)
    {
        var shown = Stopwatch.StartNew();

        // Telemetry is set up and the settings files are read on background threads, all at once,
        // so the splash keeps drawing. Only the view models are made on the UI thread.
        splash.Status = "Loading telemetry, certificates, MQTT clients and notes…";
        var monitor = new MachineMonitor();
        var telemetryTask = Task.Run(() => new TelemetryHost(monitor, MqttMetrics.MeterName));
        var certificatesTask = Task.Run(() => CertificateStore.Load());
        var mqttSettingsTask = Task.Run(MqttSettings.Load);
        var notesTask = Task.Run(NoteStore.Load);
        var passwordSettingsTask = Task.Run(PasswordSettings.Load);
        var dashboardsTask = Task.Run(DashboardStore.Load);
        var menuLayoutTask = Task.Run(MenuLayout.Load);
        await Task.WhenAll(telemetryTask, certificatesTask, mqttSettingsTask, notesTask, passwordSettingsTask, dashboardsTask, menuLayoutTask);
        var telemetry = telemetryTask.Result;

        splash.Status = "Opening…";
        await Rendered();
        var main = new MainViewModel(monitor, certificatesTask.Result, mqttSettingsTask.Result, notesTask.Result, passwordSettingsTask.Result, dashboardsTask.Result, menuLayoutTask.Result, themes);
        _ = main.Inbox.StartAsync();
        var restart = false;
        main.RestartRequested = () =>
        {
            restart = true;
            desktop.Shutdown();
        };
        desktop.Exit += (_, _) =>
        {
            main.SaveState();
            telemetry.Dispose();
            monitor.Dispose();
            if (restart)
                StartAgain();
        };

        HelpContext.Opener = topic => HelpWindow.Open(main.Help, topic ?? main.CurrentHelpTopic);

        // F1 works in every window, dialogs included, and opens the topic that window names.
        InputElement.KeyDownEvent.AddClassHandler<Window>((source, e) =>
        {
            if (e.Key != Key.F1 || source is HelpWindow)
                return;

            e.Handled = true;
            HelpContext.Open(source);
        });

        var window = new MainWindow { DataContext = main };
        if (shown.Elapsed < MinimumSplashTime)
            await Task.Delay(MinimumSplashTime - shown.Elapsed);

        // The main window takes over first, so closing the splash does not end the app.
        desktop.MainWindow = window;
        window.Show();
        window.Activate();
        splash.Close();
    }

    // The new run waits for this one to be gone, and carries out the restore before anything is read.
    private static void StartAgain()
    {
        var arguments = Environment.GetCommandLineArgs();
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };

        // Run through dotnet, the first argument is the app itself rather than dotnet.
        var skip = Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet" ? 0 : 1;
        foreach (var argument in arguments.Skip(skip))
            start.ArgumentList.Add(argument);

        try
        {
            Process.Start(start);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // The restore waits for the next time the app is started by hand.
        }
    }

    // Lets the splash draw its new status before the work that follows holds up the UI thread.
    private static Task Rendered() => Task.Delay(50);

    private void OnAboutClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
            return;

        new AboutWindow().ShowDialog(owner);
    }

    private void OnHelpClick(object? sender, EventArgs e) => HelpContext.Open(null);
}