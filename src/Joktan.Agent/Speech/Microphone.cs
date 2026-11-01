using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Agent.Resources;

namespace Joktan.Agent.Speech;

/// <summary>Records what is said, as 16 kHz mono samples.</summary>
public interface IMicrophone : IDisposable
{
    /// <summary>Raised on a background thread when a recording reaches its longest.</summary>
    event Action? LimitReached;

    /// <summary>Starts recording. Throws <see cref="InvalidOperationException"/> when there is no microphone to open.</summary>
    void Start();

    Task<float[]> StopAsync();
}

/// <summary>Records through OpenAL Soft, whose native library ships with the app.</summary>
public sealed class Microphone : IMicrophone
{
    private const int SampleRate = 16000;
    private const int Mono16 = 0x1101;
    private const int CaptureSamples = 0x312;
    private const string Library = "joktan_openal";

    // A safeguard: past this, a forgotten microphone stops by itself.
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    /// <summary>Makes the microphone to record with. Replaced in tests, so that no real one opens.</summary>
    public static Func<IMicrophone> Create { get; set; } = () => new Microphone();

    private readonly List<short> _samples = [];
    private nint _device;
    private CancellationTokenSource? _stop;
    private Task? _reading;

    static Microphone() => NativeLibrary.SetDllImportResolver(typeof(Microphone).Assembly, Resolve);

    public event Action? LimitReached;

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Library)
            return 0;

        // A Windows package keeps it with Whisper's libraries, in a folder beside the exe.
        var packaged = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "soft_oal.dll");
        if (OperatingSystem.IsWindows() && NativeLibrary.TryLoad(packaged, out var handle))
            return handle;
        return NativeLibrary.TryLoad(OperatingSystem.IsWindows() ? "soft_oal" : "libopenal", assembly, path, out handle) ? handle : 0;
    }

    public void Start()
    {
        try
        {
            _device = alcCaptureOpenDevice(null, SampleRate, Mono16, SampleRate * 2);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new InvalidOperationException(Strings.Microphone_TheAppSSoundRecordingLibrary, e);
        }

        if (_device == 0)
            throw new InvalidOperationException(Strings.Microphone_NoMicrophoneCouldBeOpened);

        _samples.Clear();
        alcCaptureStart(_device);
        _stop = new CancellationTokenSource();
        _reading = Task.Run(() => ReadAsync(_stop.Token));
    }

    private async Task ReadAsync(CancellationToken stop)
    {
        var limit = (int)(Longest.TotalSeconds * SampleRate);
        while (!stop.IsCancellationRequested)
        {
            Drain();
            if (_samples.Count >= limit)
            {
                LimitReached?.Invoke();
                return;
            }

            try
            {
                await Task.Delay(100, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Drain()
    {
        alcGetIntegerv(_device, CaptureSamples, 1, out var available);
        if (available <= 0)
            return;
        var buffer = new short[available];
        alcCaptureSamples(_device, buffer, available);
        _samples.AddRange(buffer);
    }

    public async Task<float[]> StopAsync()
    {
        if (_device == 0)
            return [];

        _stop?.Cancel();
        if (_reading is { } reading)
            await reading;
        Drain();
        Close();

        var samples = new float[_samples.Count];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = _samples[i] / 32768f;
        _samples.Clear();
        return samples;
    }

    private void Close()
    {
        if (_device == 0)
            return;
        alcCaptureStop(_device);
        alcCaptureCloseDevice(_device);
        _device = 0;
    }

    public void Dispose()
    {
        _stop?.Cancel();
        try
        {
            _reading?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // It was only reading; what it had is not wanted now.
        }

        Close();
        _stop?.Dispose();
        _stop = null;
    }

    [DllImport(Library)]
    private static extern nint alcCaptureOpenDevice([MarshalAs(UnmanagedType.LPUTF8Str)] string? name, uint frequency, int format, int bufferSize);

    [DllImport(Library)]
    private static extern void alcCaptureStart(nint device);

    [DllImport(Library)]
    private static extern void alcCaptureStop(nint device);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool alcCaptureCloseDevice(nint device);

    [DllImport(Library)]
    private static extern void alcGetIntegerv(nint device, int parameter, int size, out int data);

    [DllImport(Library)]
    private static extern void alcCaptureSamples(nint device, short[] buffer, int samples);
}
