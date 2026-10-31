using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;

namespace Joktan.Mqtt.ViewModels;

/// <summary>One message in the Messages panel.</summary>
public partial class ChatRowViewModel(ChatMessage message, bool isOwn) : ViewModelBase
{
    public ChatMessage Message { get; } = message;

    public bool IsOwn { get; } = isOwn;

    public string Name => Message.Name;

    public string Text => Message.Text;

    public string Time => InboxViewModel.TimeText(Message.Sent);

    public string Initial => InboxViewModel.InitialOf(Message.Name);

    /// <summary>Says a message went to one person, or is empty for one to everyone.</summary>
    public string DirectText => Message.To is not { } to ? "" : IsOwn ? $"to @{to}" : "to you";

    public bool IsDirect => Message.To is not null;

    public string ReplyLine => Message.ReplyName is { } name ? $"@{name}: {Message.ReplyText}" : "";

    public bool IsReply => Message.ReplyName is not null;

    /// <summary>Someone without a name cannot be written to.</summary>
    public bool CanReply => !IsOwn && Message.Name != MessagingSettings.AnonymousName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    public partial IImage? Avatar { get; set; }

    public bool HasAvatar => Avatar is not null;

    /// <summary>The words to mark, when the search box led here, while the panel stays open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFound))]
    public partial IReadOnlyList<string>? FoundWords { get; set; }

    public bool IsFound => FoundWords is not null;
}

/// <summary>One alert in the Alerts panel.</summary>
public sealed partial class AlertRowViewModel(AlertEntry alert) : ViewModelBase
{
    /// <summary>Whether the level filter lets it through.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    /// <summary>The words to mark, when the search box led here, while the panel stays open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFound))]
    public partial IReadOnlyList<string>? FoundWords { get; set; }

    public bool IsFound => FoundWords is not null;

    public string Level => Alert.Severity.ToString();

    public AlertEntry Alert { get; } = alert;

    public string Title => Alert.Title;

    public string Text => Alert.Text;

    public bool HasText => Alert.Text.Length > 0;

    public string Time => InboxViewModel.TimeText(Alert.Received);

    public bool IsWarning => Alert.Severity == AlertSeverity.Warning;

    public bool IsCritical => Alert.Severity == AlertSeverity.Critical;
}

/// <summary>
/// The Messages and Alerts panels of the top bar: what goes through the alerts and messages server.
/// Specified in docs/features/alerts-and-messages/spec.md.
/// </summary>
public partial class InboxViewModel : ViewModelBase
{
    /// <summary>How many messages, and how many alerts, are kept.</summary>
    public const int MaxKept = 200;

    /// <summary>How many times to try again when Joktan starts and cannot connect.</summary>
    public const int MaxStartRetries = 3;

    private const string SubscriptionOwner = "messaging";
    private const string AllLevels = "All levels";

    private readonly MessagingHistory _history;
    private readonly HashSet<string> _messageIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IImage> _pictures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _personTimer;

    /// <param name="history">The history, if already read; otherwise it is read here.</param>
    public InboxViewModel(MqttViewModel mqtt, MessagingHistory? history = null)
    {
        Mqtt = mqtt;
        _history = history ?? MessagingHistory.Load();
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => SaveState());
        _personTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            _personTimer!.Stop();
            _ = PublishPersonAsync();
        });
        _saveTimer.Stop();
        _personTimer.Stop();

        foreach (var message in _history.Messages)
        {
            _messageIds.Add(message.Id);
            Messages.Add(new ChatRowViewModel(message, message.Sender == Client.ClientId));
            Remember(message);
        }

        foreach (var alert in _history.Alerts)
            Alerts.Add(new AlertRowViewModel(alert));

        UseOwnPicture();
        UpdateUnread();
        Client.PropertyChanged += OnClientChanged;
        Client.Connection.MessageReceived += OnMessageReceived;
        _ = Subscribe();
    }

    public MqttViewModel Mqtt { get; }

    /// <summary>The server's client, which owns its connection.</summary>
    public MqttClientViewModel Client => Mqtt.MessagingClient;

    /// <summary>The messages, oldest first.</summary>
    public ObservableCollection<ChatRowViewModel> Messages { get; } = [];

    /// <summary>The alerts, newest first.</summary>
    public ObservableCollection<AlertRowViewModel> Alerts { get; } = [];

    /// <summary>Opens the page that sets up the server. Set by the app.</summary>
    public Action? OpenSettings { get; set; }

    public int MaxMessageLength => MessagingWire.MaxTextLength;

    /// <summary>The levels an alert can be sent at.</summary>
    public IReadOnlyList<AlertSeverity> AlertLevels { get; } = Enum.GetValues<AlertSeverity>();

    /// <summary>What the Alerts panel can be limited to.</summary>
    public IReadOnlyList<string> AlertFilters { get; } = [AllLevels, .. Enum.GetNames<AlertSeverity>()];

    /// <summary>Raised when the draft is ready to be added to, after Reply.</summary>
    public event Action? DraftFocusRequested;

    /// <summary>Raised when the Messages panel should open, to show what the search box found.</summary>
    public event Action? MessagesPanelRequested;

    /// <summary>Raised when the Alerts panel should open, to show what the search box found.</summary>
    public event Action? AlertsPanelRequested;

    /// <summary>The message the search box led to, while its panel is open.</summary>
    [ObservableProperty]
    public partial ChatRowViewModel? FoundMessage { get; private set; }

    /// <summary>The alert the search box led to, while its panel is open.</summary>
    [ObservableProperty]
    public partial AlertRowViewModel? FoundAlert { get; private set; }

    public bool HasMessages => Messages.Count > 0;

    public bool HasAlerts => Alerts.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyPropertyChangedFor(nameof(Suggestions), nameof(HasSuggestions))]
    public partial string Draft { get; set; } = "";

    /// <summary>The message being replied to, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReplyingText), nameof(IsReplying))]
    public partial ChatRowViewModel? ReplyingTo { get; private set; }

    public string ReplyingText => ReplyingTo is { } row ? $"Replying to @{row.Name}: {Clip(row.Text)}" : "";

    public bool IsReplying => ReplyingTo is not null;

    /// <summary>The people whose names start with what follows an @ at the start of the draft.</summary>
    public IReadOnlyList<string> Suggestions => Draft.StartsWith('@') && !Draft.Any(char.IsWhiteSpace)
        ? PeopleNames.Where(n => n.StartsWith(Draft[1..].ToLowerInvariant(), StringComparison.Ordinal)).ToList()
        : [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>The text of an alert about to be sent.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendAlertCommand))]
    public partial string AlertDraft { get; set; } = "";

    [ObservableProperty]
    public partial AlertSeverity AlertLevel { get; set; }

    /// <summary>Which level the Alerts panel shows.</summary>
    [ObservableProperty]
    public partial string AlertFilter { get; set; } = AllLevels;

    /// <summary>The highest level among the unread alerts is warning, so the bell's dot says so.</summary>
    [ObservableProperty]
    public partial bool IsUnreadWarning { get; private set; }

    /// <summary>The unread alerts are all for information, so the bell's dot says so.</summary>
    [ObservableProperty]
    public partial bool IsUnreadInfo { get; private set; }

    /// <summary>Why the last message could not be sent.</summary>
    [ObservableProperty]
    public partial string? SendError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadMessages), nameof(MessagesTip))]
    public partial int UnreadMessages { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadAlerts), nameof(AlertsTip))]
    public partial int UnreadAlerts { get; private set; }

    public bool HasUnreadMessages => UnreadMessages > 0;

    public bool HasUnreadAlerts => UnreadAlerts > 0;

    public string MessagesTip => UnreadMessages > 0 ? $"Messages: {UnreadMessages} unread" : "Messages";

    public string AlertsTip => UnreadAlerts > 0 ? $"Alerts: {UnreadAlerts} unread" : "Alerts";

    /// <summary>Whether the Messages panel is open. What arrives while it is counts as read.</summary>
    [ObservableProperty]
    public partial bool IsMessagesOpen { get; set; }

    /// <summary>Whether the Alerts panel is open. What arrives while it is counts as read.</summary>
    [ObservableProperty]
    public partial bool IsAlertsOpen { get; set; }

    private string Prefix => Mqtt.Messaging.TopicPrefix;

    private bool CanSend => Client.IsConnected && Draft.Trim().Length > 0;

    private bool CanSendAlert => Client.IsConnected && AlertDraft.Trim().Length > 0;

    // Everyone seen on the server but the user, as the people topic and messages show them.
    private IEnumerable<string> PeopleNames => _names.Where(p => p.Key != Client.ClientId).Select(p => p.Value)
        .Distinct().OrderBy(n => n, StringComparer.Ordinal);

    // The user's name when it can be written to, or null when it is unset or breaks the rule.
    private string? OwnAddress => Mqtt.Messaging.DisplayName is { Length: > 0 } name && MessagingSettings.CheckName(name) is null ? name : null;

    /// <summary>How long to wait between tries when Joktan starts.</summary>
    public TimeSpan StartRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Connects when Joktan starts, unless switched off, trying again a few times if it fails. Anything
    /// but a failure ends it, so pressing Cancel or Connect meanwhile is left to stand.
    /// </summary>
    public async Task StartAsync()
    {
        if (!Mqtt.Messaging.ConnectOnStart)
            return;

        for (var retries = MaxStartRetries; ; retries--)
        {
            if (!Client.ConnectCommand.CanExecute(null))
                return;

            await Client.ConnectCommand.ExecuteAsync(null);
            if (!Client.IsFailed || retries == 0)
                return;

            await Task.Delay(StartRetryDelay);
            if (!Client.IsFailed)
                return;
        }
    }

    /// <summary>Listens on the topics of the prefix in use. Returns why it could not, or null.</summary>
    public Task<string?> Subscribe() => Client.Connection.SetSubscriptionsAsync(SubscriptionOwner, MessagingWire.Filters(Prefix, OwnAddress));

    /// <summary>Whether someone else on the server uses this name. Messages to it reach them both.</summary>
    public bool IsNameTaken(string name) => PeopleNames.Contains(name);

    /// <summary>Tells the others about a new name or picture, once the user has stopped changing them.</summary>
    public void IdentityChanged()
    {
        UseOwnPicture();
        _ = Subscribe();
        _personTimer.Stop();
        _personTimer.Start();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var draft = Draft.Trim();
        string? to = null;

        // Anything starting with @ goes to one person or nowhere, so a mistyped name never reaches everyone.
        if (draft.StartsWith('@'))
        {
            (to, draft, var problem) = ReadDirect(draft);
            if (problem is not null)
            {
                SendError = problem;
                return;
            }
        }

        var message = new ChatMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Sender = Client.ClientId,
            Name = OwnAddress ?? MessagingSettings.AnonymousName,
            Text = draft,
            Sent = DateTimeOffset.Now,
            To = to,
            ReplyName = ReplyingTo?.Name,
            ReplyText = ReplyingTo is { } row ? Clip(row.Text) : null,
            IsRead = true,
        };

        var topic = to is null ? MessagingWire.MessagesTopic(Prefix) : MessagingWire.DirectTopic(Prefix, to);
        var (ok, error) = await Client.Connection.PublishAsync(topic, MessagingWire.Write(message), 1, false);
        SendError = ok ? null : error ?? "The message could not be sent";
        if (!ok)
            return;

        Draft = "";
        ReplyingTo = null;
        Add(message);
    }

    // Reads "@name text". Returns why it cannot be sent, if it cannot.
    private (string? To, string Text, string? Problem) ReadDirect(string draft)
    {
        var space = draft.IndexOfAny([' ', '\t', '\n', '\r']);
        var name = (space < 0 ? draft[1..] : draft[1..space]).ToLowerInvariant();
        var text = space < 0 ? "" : draft[space..].Trim();
        if (name.Length == 0 || MessagingSettings.CheckName(name) is not null)
            return (null, text, $"@{name} is not a name, so this was not sent");
        if (text.Length == 0)
            return (null, text, $"Write something after @{name}");
        if (OwnAddress is null)
            return (null, text, "Choose a display name under Settings / Alerts and Messages first, so that they can answer");
        if (!PeopleNames.Contains(name))
            return (null, text, $"Nobody called @{name} has been seen on this server, so this was not sent");
        return (name, text, null);
    }

    [RelayCommand]
    private void Reply(ChatRowViewModel row)
    {
        if (!row.CanReply)
            return;

        ReplyingTo = row;
        Draft = $"@{row.Name} ";
        DraftFocusRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelReply()
    {
        ReplyingTo = null;
        Draft = "";
    }

    [RelayCommand]
    private void PickSuggestion(string name)
    {
        Draft = $"@{name} ";
        DraftFocusRequested?.Invoke();
    }

    // The server brings it back, which is what puts it in the list here as anywhere else.
    [RelayCommand(CanExecute = nameof(CanSendAlert))]
    private async Task SendAlertAsync()
    {
        var topic = $"{MessagingWire.AlertsTopic(Prefix)}/{OwnAddress ?? MessagingSettings.AnonymousName}";
        var (ok, error) = await Client.Connection.PublishAsync(topic, MessagingWire.WriteAlert(AlertDraft.Trim(), AlertLevel), 1, false);
        SendError = ok ? null : error ?? "The alert could not be sent";
        if (ok)
            AlertDraft = "";
    }

    [RelayCommand]
    private void ClearMessages()
    {
        Messages.Clear();
        _messageIds.Clear();
        Changed();
    }

    [RelayCommand]
    private void ClearAlerts()
    {
        Alerts.Clear();
        Changed();
    }

    [RelayCommand]
    private void ShowSettings() => OpenSettings?.Invoke();

    /// <summary>The messages with all the words in their sender or text, newest first, for the search box of the top bar.</summary>
    public IEnumerable<SearchHit> SearchMessages(IReadOnlyList<string> words) => Messages.Reverse()
        .Where(row => TextSearch.HasAll(words, row.Name, row.Text))
        .Select(row => new SearchHit("Messages", row.Name, $"{row.Time} · {TextSearch.Snippet(TextSearch.Lines(row.Text), words)}", () => ShowMessage(row, words)));

    /// <summary>The alerts with all the words in their title, level or text, newest first, for the search box of the top bar. The panel's filter does not limit them.</summary>
    public IEnumerable<SearchHit> SearchAlerts(IReadOnlyList<string> words) => Alerts
        .Where(row => TextSearch.HasAll(words, row.Title, row.Level, row.Text))
        .Select(row => new SearchHit("Alerts", row.Title, row.HasText ? $"{row.Time} · {row.Level} · {TextSearch.Snippet(TextSearch.Lines(row.Text), words)}" : $"{row.Time} · {row.Level}", () => ShowAlert(row, words)));

    private void ShowMessage(ChatRowViewModel row, IReadOnlyList<string> words)
    {
        FoundMessage = row;
        row.FoundWords = words;
        MessagesPanelRequested?.Invoke();
    }

    // A filter that hides the alert would make it look as though it was not there.
    private void ShowAlert(AlertRowViewModel row, IReadOnlyList<string> words)
    {
        if (!row.IsShown)
            AlertFilter = AllLevels;

        FoundAlert = row;
        row.FoundWords = words;
        AlertsPanelRequested?.Invoke();
    }

    partial void OnFoundMessageChanged(ChatRowViewModel? oldValue, ChatRowViewModel? newValue) => oldValue?.FoundWords = null;

    partial void OnFoundAlertChanged(AlertRowViewModel? oldValue, AlertRowViewModel? newValue) => oldValue?.FoundWords = null;

    /// <summary>Writes the history now. It is otherwise written shortly after a change.</summary>
    public void SaveState()
    {
        _saveTimer.Stop();
        _history.Messages = Messages.Select(r => r.Message).ToList();
        _history.Alerts = Alerts.Select(r => r.Alert).ToList();
        _history.Save();
    }

    /// <summary>A date and time for a list: only the time for today.</summary>
    public static string TimeText(DateTimeOffset time)
    {
        var local = time.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm", CultureInfo.InvariantCulture) : local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>What stands in for a picture: the first letter of a name.</summary>
    public static string InitialOf(string name) => name.Trim() is { Length: > 0 } trimmed
        ? StringInfo.GetNextTextElement(trimmed).ToUpperInvariant()
        : "?";

    private static string Clip(string text) => text.Length > MessagingWire.MaxReplyLength ? text[..MessagingWire.MaxReplyLength] : text;

    partial void OnAlertFilterChanged(string value)
    {
        foreach (var row in Alerts)
            row.IsShown = Passes(row);
    }

    private bool Passes(AlertRowViewModel row) => AlertFilter == AllLevels || AlertFilter == row.Level;

    private void Remember(ChatMessage message)
    {
        if (message.Name != MessagingSettings.AnonymousName)
            _names[message.Sender] = message.Name;
    }

    partial void OnIsMessagesOpenChanged(bool value)
    {
        if (!value)
        {
            FoundMessage = null;
            return;
        }

        foreach (var row in Messages)
            row.Message.IsRead = true;
        Changed();
    }

    partial void OnIsAlertsOpenChanged(bool value)
    {
        if (!value)
        {
            FoundAlert = null;
            return;
        }

        foreach (var row in Alerts)
            row.Alert.IsRead = true;
        Changed();
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MqttClientViewModel.IsConnected))
            return;

        SendCommand.NotifyCanExecuteChanged();
        SendAlertCommand.NotifyCanExecuteChanged();
        if (Client.IsConnected)
            _ = PublishPersonAsync();
    }

    private Task PublishPersonAsync()
    {
        if (!Client.IsConnected)
            return Task.CompletedTask;

        return Client.Connection.PublishAsync(MessagingWire.PersonTopic(Prefix, Client.ClientId), MessagingWire.WritePerson(OwnAddress ?? MessagingSettings.AnonymousName, ReadOwnPicture()), 1, true);
    }

    // Arrives on the MQTT client's thread.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (message.Topic.StartsWith(Prefix + "/", StringComparison.Ordinal))
            Dispatcher.UIThread.Post(() => Receive(message));
    }

    private void Receive(ReceivedMessage message)
    {
        var now = DateTimeOffset.Now;
        if (message.Topic == MessagingWire.MessagesTopic(Prefix))
        {
            if (MessagingWire.ReadMessage(message.Payload, now) is { } chat)
                Add(chat);
        }
        else if (OwnAddress is { } own && message.Topic == MessagingWire.DirectTopic(Prefix, own))
        {
            // Only messages to the user get here. A tab on the Brokers pages can bring in others', which are not for this list.
            if (MessagingWire.ReadMessage(message.Payload, now) is { } chat)
            {
                chat.To = own;
                Add(chat);
            }
        }
        else if (TopicRules.Matches($"{MessagingWire.AlertsTopic(Prefix)}/#", message.Topic))
        {
            if (MessagingWire.ReadAlert(message.Topic, Prefix, message.Payload, now) is { } alert)
                Add(alert, message.Retain);
        }
        else if (TopicRules.Matches($"{Prefix}/people/+", message.Topic))
        {
            var person = Uri.UnescapeDataString(message.Topic[(message.Topic.LastIndexOf('/') + 1)..]);
            if (person == Client.ClientId)
                return;

            UsePicture(person, Decode(MessagingWire.ReadPersonPicture(message.Payload)));
            if (MessagingWire.ReadPersonName(message.Payload) is { } name)
                _names[person] = name;
            else
                _names.Remove(person);
            OnPropertyChanged(nameof(Suggestions));
            OnPropertyChanged(nameof(HasSuggestions));
        }
    }

    // A message can come back from the server, and more than once.
    private void Add(ChatMessage message)
    {
        if (!_messageIds.Add(message.Id))
            return;

        Remember(message);
        var isOwn = message.Sender == Client.ClientId;
        message.IsRead |= isOwn || IsMessagesOpen;
        var row = new ChatRowViewModel(message, isOwn) { Avatar = _pictures.GetValueOrDefault(message.Sender) };
        Messages.Add(row);
        while (Messages.Count > MaxKept)
        {
            _messageIds.Remove(Messages[0].Message.Id);
            Messages.RemoveAt(0);
        }

        Changed();
    }

    // A retained alert comes again on every connection, so it counts once.
    private void Add(AlertEntry alert, bool retained)
    {
        if (retained && Alerts.Any(r => r.Alert.Topic == alert.Topic && r.Alert.Title == alert.Title && r.Alert.Text == alert.Text))
            return;

        alert.IsRead = IsAlertsOpen;
        var row = new AlertRowViewModel(alert);
        row.IsShown = Passes(row);
        Alerts.Insert(0, row);
        while (Alerts.Count > MaxKept)
            Alerts.RemoveAt(Alerts.Count - 1);
        Changed();
    }

    private void Changed()
    {
        UpdateUnread();
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(HasAlerts));
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void UpdateUnread()
    {
        UnreadMessages = Messages.Count(r => !r.Message.IsRead);
        UnreadAlerts = Alerts.Count(r => !r.Alert.IsRead);
        var highest = Alerts.Where(r => !r.Alert.IsRead).Select(r => r.Alert.Severity).DefaultIfEmpty(AlertSeverity.Critical).Max();
        IsUnreadWarning = highest == AlertSeverity.Warning;
        IsUnreadInfo = highest == AlertSeverity.Info;
    }

    private void UseOwnPicture() => UsePicture(Client.ClientId, Decode(ReadOwnPicture()));

    private void UsePicture(string person, IImage? picture)
    {
        if (picture is null)
            _pictures.Remove(person);
        else
            _pictures[person] = picture;
        foreach (var row in Messages.Where(r => r.Message.Sender == person))
            row.Avatar = picture;
    }

    private static byte[]? ReadOwnPicture()
    {
        try
        {
            return File.Exists(MessagingSettings.AvatarPath) ? File.ReadAllBytes(MessagingSettings.AvatarPath) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Bitmap? Decode(byte[]? picture)
    {
        if (picture is null)
            return null;

        try
        {
            return new Bitmap(new MemoryStream(picture));
        }
        catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
