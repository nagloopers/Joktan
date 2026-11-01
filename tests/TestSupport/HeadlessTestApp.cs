using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

// Each test's application takes over Avalonia's dispatcher, so nothing else may touch it meanwhile.
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Joktan.Tests.TestSupport;

/// <summary>
/// An application with Joktan's own theme and no windowing system, for tests of views and controls.
/// It draws with Skia, so that what a control renders can be run and looked at.
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude((Uri?)null) { Source = new Uri("avares://Joktan.Core/Styles/Tokens.axaml") });
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml") });
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://Joktan.Core/Styles/Shell.axaml") });
        Styles.Add(new StyleInclude((Uri?)null) { Source = new Uri("avares://Joktan.Core/Styles/Markdown.axaml") });
    }
}

/// <summary>Helps for tests that show a view in the headless application.</summary>
public static class Headless
{
    // Each test gets an application and dispatcher of its own, so that nothing a test leaves behind reaches the next.
    private static readonly Lazy<HeadlessUnitTestSession> Session =
        new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessTestApp)));

    /// <summary>Runs a test on the UI thread of the headless application.</summary>
    public static Task Run(Action test) => Run(() =>
    {
        test();
        return Task.CompletedTask;
    });

    /// <summary>Runs a test on the UI thread of the headless application.</summary>
    public static async Task Run(Func<Task> test)
    {
        // A connection left over from the last test can reach for Avalonia's dispatcher just as the
        // application starts, which stops it starting. The test has not begun then, so it can try again.
        for (var attempt = 1; ; attempt++)
        {
            var started = false;
            try
            {
                // Through the overload for Task<T>; the one for a plain Func<T> would not wait for the test to finish.
                await Session.Value.Dispatch(async () =>
                {
                    started = true;
                    await test();
                    return true;
                }, CancellationToken.None);
                return;
            }
            catch (InvalidOperationException) when (!started && attempt < 5)
            {
                await Task.Delay(50, CancellationToken.None);
            }
        }
    }

    /// <summary>Shows a control in a window of its own and lets it lay out and draw.</summary>
    public static Window Show(Control content, object? dataContext = null, double width = 1200, double height = 900)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        if (dataContext is not null)
            window.DataContext = dataContext;
        return Show(window);
    }

    /// <summary>Shows a window and lets it lay out and draw.</summary>
    public static Window Show(Window window)
    {
        window.Show();
        Settle();
        window.CaptureRenderedFrame();
        Settle();
        return window;
    }

    /// <summary>Clicks the middle of a control that is showing.</summary>
    public static void Click(Control control, MouseButton button = MouseButton.Left)
    {
        var window = (Window)TopLevel.GetTopLevel(control)!;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, button);
        window.MouseUp(point, button);
        Settle();
    }

    /// <summary>The first control of a type below another, in what is showing.</summary>
    public static T Find<T>(Visual root, Func<T, bool>? matches = null) where T : Visual =>
        All<T>(root).First(c => matches?.Invoke(c) ?? true);

    /// <summary>The controls of a type below another, in what is showing.</summary>
    public static IEnumerable<T> All<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>();

    /// <summary>How many pixels of what a window last drew pass a test.</summary>
    public static int CountPixels(Window window, Func<Color, bool> matches)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var size = frame.PixelSize;
        var bytes = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        var isRgba = frame.Format == PixelFormats.Rgba8888;
        var count = 0;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            var (red, blue) = isRgba ? (bytes[i], bytes[i + 2]) : (bytes[i + 2], bytes[i]);
            if (matches(Color.FromArgb(bytes[i + 3], red, bytes[i + 1], blue)))
                count++;
        }

        return count;
    }

    /// <summary>Runs what is waiting on the UI thread, timers aside.</summary>
    public static void Settle() => Dispatcher.UIThread.RunJobs();

    /// <summary>Keeps the UI thread running until a condition holds, for work that finishes in the background.</summary>
    public static async Task Until(Func<bool> condition, int timeoutMs = 10000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > timeoutMs)
                throw new TimeoutException("The condition did not hold in time.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, CancellationToken.None);
        }
    }
}
