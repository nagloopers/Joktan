using System;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// One row of the folder message list.
/// </summary>
public class FolderMessageRowViewModel : ViewModelBase
{
    private const string None = "–";

    private readonly Action<FolderMessageRowViewModel, bool> _setEnabled;

    public FolderMessageRowViewModel(FolderMessage message, Action<FolderMessageRowViewModel, bool> setEnabled)
    {
        Message = message;
        _setEnabled = setEnabled;
    }

    public FolderMessage Message { get; }

    public string Topic => Message.Topic.Length == 0 ? None : Message.Topic;

    public string File => Message.FileName;

    public string Schedule => Message.IsValid ? Message.Settings.Schedule.Describe() : None;

    public string NextSend => Format(Message.NextDue);

    public string LastSent => Format(Message.LastSent);

    public string Sent => Message.SentCount.ToString();

    public string State => Message.State switch
    {
        FolderMessageState.Invalid => string.Format(Strings.FolderMessageRow_Invalid, Message.Error),
        FolderMessageState.Manual => Strings.FolderMessageRow_NoSchedule,
        FolderMessageState.Waiting => Strings.FolderMessageRow_WaitingForConnection,
        var state => state.Text(),
    };

    public bool IsInvalid => !Message.IsValid;

    /// <summary>Only a message with a schedule can be switched on.</summary>
    public bool CanSwitch => Message.HasSchedule;

    public string SwitchTip => Message.HasSchedule
        ? Strings.FolderMessageRow_SendThisMessageOnItsSchedule
        : IsInvalid ? Strings.FolderMessageRow_ThisMessageCannotBeSent : Strings.FolderMessageRow_GiveTheMessageAScheduleWith;

    /// <summary>The switch in the grid. Changing it saves the message's settings.</summary>
    public bool IsOn
    {
        get => Message.HasSchedule && Message.Settings.Enabled;
        set
        {
            if (value != IsOn && CanSwitch)
                _setEnabled(this, value);
        }
    }

    /// <summary>Re-reads every column after the message's progress has changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private static string Format(DateTime? time)
    {
        if (time is not { } t)
            return None;
        return t.Date == DateTime.Today ? t.ToString("HH:mm:ss") : t.ToString("yyyy-MM-dd HH:mm");
    }
}
