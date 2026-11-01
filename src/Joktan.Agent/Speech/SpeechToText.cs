using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Whisper.net;

namespace Joktan.Agent.Speech;

/// <summary>
/// Turns speech into text on this computer with Whisper. The model ships beside the app, in its models folder, and
/// the app has no code that downloads one, so nothing here reaches the network.
/// </summary>
public sealed partial class SpeechToText : IDisposable
{
    private readonly object _gate = new();
    private Task<WhisperFactory>? _loading;

    public static string ModelPath => Path.Combine(AppContext.BaseDirectory, "models", "ggml-base.en.bin");

    public static bool IsAvailable => File.Exists(ModelPath);

    /// <summary>Starts reading the model, which takes some seconds the first time, so that the first words do not wait for it.</summary>
    public Task LoadAsync() => Factory();

    private Task<WhisperFactory> Factory()
    {
        lock (_gate)
            return _loading ??= Task.Run(() => WhisperFactory.FromPath(ModelPath));
    }

    public async Task<string> TranscribeAsync(float[] samples)
    {
        var factory = await Factory();
        return await Task.Run(async () =>
        {
            await using var processor = factory.CreateBuilder().WithLanguage("en").Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples))
                text.Append(segment.Text);

            // Whisper names what is not speech, such as [BLANK_AUDIO] or (wind blowing).
            return NotSpeech().Replace(text.ToString(), " ").Trim();
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_loading is { IsCompletedSuccessfully: true } loading)
                loading.Result.Dispose();
            _loading = null;
        }
    }

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex NotSpeech();
}
