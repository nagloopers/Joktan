using System;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Pomodoro.Resources;

namespace Joktan.Pomodoro.ViewModels;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak,
}

/// <summary>
/// Pomodoro: periods of focus with breaks between them. Specified in docs/features/pomodoro/spec.md.
/// </summary>
public partial class PomodoroViewModel : PageViewModel
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly PomodoroSettings _settings;
    private readonly DispatcherTimer _timer;

    // The time left comes from when the phase ends, not from counting ticks, so that it stays right when ticks come late or the computer sleeps.
    private DateTimeOffset _endsAt;
    private TimeSpan _length;
    private int _focusesDone;

    public PomodoroViewModel(PomodoroSettings settings) : base(Strings.Pomodoro_Pomodoro, Strings.Pomodoro_PeriodsOfFocusWithBreaksBetween)
    {
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();
        Enter(PomodoroPhase.Focus);
    }

    /// <summary>Where the time comes from. Tests set their own.</summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>Adds an alert with this title and text. Set by the app.</summary>
    public Action<string, string>? AddAlert { get; set; }

    /// <summary>Reads this text aloud. Set by the app.</summary>
    public Action<string>? Speak { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhaseName), nameof(TopBarTip))]
    public partial PomodoroPhase Phase { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartPauseText))]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingText), nameof(Progress), nameof(IsPartway), nameof(StartPauseText), nameof(TopBarTip))]
    public partial TimeSpan Remaining { get; private set; }

    /// <summary>What happened when the last phase ended.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    /// <summary>Whether this phase has been started and not finished, so the top bar shows it.</summary>
    public bool IsPartway => Remaining < _length;

    /// <summary>The time left in whole seconds, rounded up so that it reaches 00:00 just as the phase ends.</summary>
    public string RemainingText
    {
        get
        {
            var seconds = (int)Math.Ceiling(Remaining.TotalSeconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    /// <summary>How much of this phase has passed, from 0 to 100.</summary>
    public double Progress => _length > TimeSpan.Zero ? 100 * (1 - Remaining / _length) : 0;

    public string PhaseName => Phase switch
    {
        PomodoroPhase.ShortBreak => Strings.Pomodoro_ShortBreak,
        PomodoroPhase.LongBreak => Strings.Pomodoro_LongBreak,
        _ => string.Format(Strings.Pomodoro_FocusOf, Math.Min(_focusesDone + 1, FocusesBeforeLongBreak), FocusesBeforeLongBreak),
    };

    public string StartPauseText => IsRunning ? Strings.Pomodoro_Pause : IsPartway ? Strings.Pomodoro_Resume : Strings.Pomodoro_Start;

    public string TopBarTip => string.Format(Strings.Pomodoro_PomodoroLeft, PhaseName, RemainingText);

    public int DoneToday => _settings.DoneOn == Today ? _settings.DoneCount : 0;

    public string DoneTodayText => DoneToday switch
    {
        0 => Strings.Pomodoro_NoFocusPeriodsFinishedToday,
        1 => Strings.Pomodoro_1FocusPeriodFinishedToday,
        var count => string.Format(Strings.Pomodoro_FocusPeriodsFinishedToday, count),
    };

    public int FocusMinutes
    {
        get => _settings.FocusMinutes;
        set => SetMinutes(_settings.FocusMinutes = PomodoroSettings.ClampMinutes(value));
    }

    public int ShortBreakMinutes
    {
        get => _settings.ShortBreakMinutes;
        set => SetMinutes(_settings.ShortBreakMinutes = PomodoroSettings.ClampMinutes(value));
    }

    public int LongBreakMinutes
    {
        get => _settings.LongBreakMinutes;
        set => SetMinutes(_settings.LongBreakMinutes = PomodoroSettings.ClampMinutes(value));
    }

    public int FocusesBeforeLongBreak
    {
        get => _settings.FocusesBeforeLongBreak;
        set
        {
            _settings.FocusesBeforeLongBreak = PomodoroSettings.ClampFocuses(value);
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(PhaseName));
            OnPropertyChanged(nameof(TopBarTip));
        }
    }

    public bool AutoStart
    {
        get => _settings.AutoStart;
        set
        {
            _settings.AutoStart = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool AlertAtEnd
    {
        get => _settings.AlertAtEnd;
        set
        {
            _settings.AlertAtEnd = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    public bool SpeakAtEnd
    {
        get => _settings.SpeakAtEnd;
        set
        {
            _settings.SpeakAtEnd = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    private DateOnly Today => DateOnly.FromDateTime(Clock.GetLocalNow().DateTime);

    /// <summary>Brings the time left up to date, and ends the phase if it has run out.</summary>
    public void Tick()
    {
        if (!IsRunning)
            return;

        var left = _endsAt - Clock.GetUtcNow();
        if (left > TimeSpan.Zero)
            Remaining = left;
        else
            Finish();
    }

    [RelayCommand]
    private void StartPause()
    {
        if (IsRunning)
            Pause();
        else
            Start();
    }

    /// <summary>Stops the timer and goes back to the start of this phase.</summary>
    [RelayCommand]
    private void Reset()
    {
        Pause();
        Enter(Phase);
    }

    /// <summary>Moves on without counting this phase as finished.</summary>
    [RelayCommand]
    private void Skip()
    {
        Pause();
        if (Phase == PomodoroPhase.LongBreak)
            _focusesDone = 0;

        Enter(Phase == PomodoroPhase.Focus ? PomodoroPhase.ShortBreak : PomodoroPhase.Focus);
        Message = null;
        if (AutoStart)
            Start();
    }

    private void Start()
    {
        Message = null;
        _endsAt = Clock.GetUtcNow() + Remaining;
        IsRunning = true;
        _timer.Start();
        OnPropertyChanged(nameof(DoneTodayText));
    }

    private void Pause()
    {
        _timer.Stop();
        IsRunning = false;
    }

    private void Finish()
    {
        Pause();
        PomodoroPhase next;
        string message;
        if (Phase == PomodoroPhase.Focus)
        {
            CountFocus();
            next = ++_focusesDone >= FocusesBeforeLongBreak ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
            message = next == PomodoroPhase.LongBreak ? Strings.Pomodoro_FocusIsOverTimeForA : Strings.Pomodoro_FocusIsOverTimeForAShort;
        }
        else
        {
            if (Phase == PomodoroPhase.LongBreak)
                _focusesDone = 0;
            next = PomodoroPhase.Focus;
            message = Strings.Pomodoro_TheBreakIsOverTimeTo;
        }

        Enter(next);
        if (AutoStart)
            Start();

        Message = message;
        if (AlertAtEnd)
            AddAlert?.Invoke(Title, message);
        if (SpeakAtEnd)
            Speak?.Invoke(message);
    }

    private void CountFocus()
    {
        _settings.DoneCount = DoneToday + 1;
        _settings.DoneOn = Today;
        _settings.Save();
        OnPropertyChanged(nameof(DoneToday));
        OnPropertyChanged(nameof(DoneTodayText));
    }

    // A new length shows at once on a phase that has not begun; one under way keeps the length it began with.
    private void SetMinutes(int _, [CallerMemberName] string? name = null)
    {
        _settings.Save();
        OnPropertyChanged(name);
        if (!IsRunning && !IsPartway)
            Enter(Phase);
    }

    private void Enter(PomodoroPhase phase)
    {
        Phase = phase;
        _length = TimeSpan.FromMinutes(phase switch
        {
            PomodoroPhase.ShortBreak => ShortBreakMinutes,
            PomodoroPhase.LongBreak => LongBreakMinutes,
            _ => FocusMinutes,
        });
        Remaining = _length;

        // The name counts focus periods, and the bar and text depend on the new length even when the time left is the same.
        OnPropertyChanged(nameof(PhaseName));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsPartway));
        OnPropertyChanged(nameof(StartPauseText));
        OnPropertyChanged(nameof(TopBarTip));
    }
}
