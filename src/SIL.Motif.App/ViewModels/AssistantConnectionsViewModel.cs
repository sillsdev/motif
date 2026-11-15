using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Assistants;

namespace SIL.Motif.App.ViewModels;

/// <summary>Controls Advanced AI mode and connects the assistants found on this computer.</summary>
public sealed partial class AssistantConnectionsViewModel : ObservableObject, IDisposable
{
    private WorkspaceContext Context { get; }
    private readonly IAssistantConnectionService _connections;
    private readonly IUriLauncher? _launcher;
    private readonly SynchronizationContext? _uiContext;
    private int _chatGptAttempt;
    private bool _disposed;

    /// <summary>Creates the connection controls from the window's preferences and assistant adapters.</summary>
    public AssistantConnectionsViewModel(WorkspaceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        _connections = context.AssistantConnections;
        _launcher = context.UriLauncher;
        _uiContext = SynchronizationContext.Current;
        if (context.ChatGptTunnel is { } tunnel)
            tunnel.StatusChanged += OnTunnelStatusChanged;
        _isEnabled = context.AdvancedAiModePreferences?.IsEnabled ?? true;
        foreach (var assistant in _connections.FindAssistants())
        {
            var choice = new AssistantChoiceViewModel(assistant);
            choice.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(AssistantChoiceViewModel.IsSelected)) return;
                OnPropertyChanged(nameof(HasSelectedAssistants));
                ConnectSelectedCommand.NotifyCanExecuteChanged();
            };
            Assistants.Add(choice);
        }
    }

    /// <summary>Stops observing the connection after this component is no longer used.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ++_chatGptAttempt;
        if (Context.ChatGptTunnel is { } tunnel) tunnel.StatusChanged -= OnTunnelStatusChanged;
    }

    /// <summary>The assistants detected by the connection service.</summary>
    public ObservableCollection<AssistantChoiceViewModel> Assistants { get; } = [];

    /// <summary>Whether Advanced AI mode is enabled for this installation.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        Context.AdvancedAiModePreferences?.SetEnabled(value);
        StatusMessage = value ? MotifAppStrings.AssistantsEnabled : MotifAppStrings.AssistantsDisabled;
    }

    /// <summary>Whether the assistant choices should appear.</summary>
    public bool ShowsAssistantList => true;

    /// <summary>Whether any supported assistant was found.</summary>
    public bool HasAssistants => Assistants.Count > 0;

    /// <summary>Whether the person checked at least one assistant.</summary>
    public bool HasSelectedAssistants => Assistants.Any(assistant => assistant.IsSelected);

    /// <summary>Whether no supported assistant was found.</summary>
    public bool HasNoAssistants => !HasAssistants;

    /// <summary>What to show when no supported assistant was found.</summary>
    public string EmptyAssistantText => MotifAppStrings.AssistantsNotFound;

    /// <summary>Whether the package version differs from the running Motif version.</summary>
    public bool HasVersionWarning => !string.IsNullOrWhiteSpace(_connections.VersionWarning);

    /// <summary>The package and server version mismatch, when one exists.</summary>
    public string VersionWarning => _connections.VersionWarning ?? string.Empty;

    /// <summary>The instructions from the last connection attempt.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    /// <summary>Whether Claude Desktop's upload and restart steps are available.</summary>
    [ObservableProperty]
    private bool _showsDesktopUpload;

    /// <summary>Whether the last connection attempt returned a message.</summary>
    public bool HasStatusMessage => StatusMessage.Length > 0;

    /// <summary>Whether at least one selected assistant can be connected.</summary>
    public bool CanConnectSelected => HasSelectedAssistants;

    /// <summary>The bundled plugin ZIP to upload in Claude Desktop.</summary>
    public string PluginZipPath => _connections.PluginZipPath;

    /// <summary>The runtime key entered for the next connection.</summary>
    [ObservableProperty]
    private string _chatGptKey = string.Empty;

    /// <summary>The tunnel identifier supplied by the person.</summary>
    [ObservableProperty]
    private string _chatGptTunnelId = string.Empty;

    /// <summary>The connection state in window language.</summary>
    [ObservableProperty]
    private string _chatGptStatus = MotifAppStrings.ChatGptNotConnected;

    /// <summary>The notice when the operating system cannot persist the key.</summary>
    [ObservableProperty]
    private string _chatGptSaveWarning = string.Empty;

    [RelayCommand]
    private async Task ConnectChatGptAsync()
    {
        if (_disposed) return;
        var attempt = ++_chatGptAttempt;
        var store = Context.ChatGptCredentials;
        var tunnel = Context.ChatGptTunnel;
        if (store is null || tunnel is null) { ChatGptStatus = MotifAppStrings.ChatGptError; return; }
        var enteredKey = ChatGptKey;
        var key = string.IsNullOrWhiteSpace(enteredKey)
            ? await Task.Run(store.Read).ConfigureAwait(true)
            : enteredKey;
        if (attempt != _chatGptAttempt) return;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ChatGptTunnelId))
        {
            ChatGptStatus = MotifAppStrings.ChatGptMissing;
            return;
        }
        if (!string.IsNullOrWhiteSpace(enteredKey))
        {
            var saved = await Task.Run(() => store.Write(enteredKey)).ConfigureAwait(true);
            if (attempt != _chatGptAttempt) return;
            ChatGptSaveWarning = saved ? string.Empty : MotifAppStrings.ChatGptKeyNotSaved;
            ChatGptKey = string.Empty;
        }
        await tunnel.ConnectAsync(key, ChatGptTunnelId).ConfigureAwait(true);
    }

    [RelayCommand]
    private void DisconnectChatGpt()
    {
        ++_chatGptAttempt;
        Context.ChatGptTunnel?.Disconnect();
    }

    private void OnTunnelStatusChanged(TunnelStatus status)
    {
        void Update()
        {
            if (_disposed) return;
            ChatGptStatus = (Context.ChatGptTunnel?.Status ?? status) switch
            {
                TunnelStatus.Connecting => MotifAppStrings.ChatGptConnecting,
                TunnelStatus.Connected => MotifAppStrings.ChatGptConnected,
                TunnelStatus.Error => MotifAppStrings.ChatGptError,
                _ => MotifAppStrings.ChatGptNotConnected,
            };
        }
        if (_uiContext is null) Update();
        else _uiContext.Post(_ => Update(), null);
    }

    /// <summary>Connects every checked assistant through its client-specific setup.</summary>
    [RelayCommand(CanExecute = nameof(CanConnectSelected))]
    private async Task ConnectSelectedAsync()
    {
        ShowsDesktopUpload = false;
        var messages = new List<string>();
        foreach (var assistant in Assistants.Where(item => item.IsSelected))
        {
            if (_disposed) return;
            try
            {
                var result = await _connections.ConnectAsync(assistant.Client).ConfigureAwait(true);
                if (_disposed) return;
                messages.Add(WindowPathText.Scrub(result.Message));
                ShowsDesktopUpload |= assistant.Client == AssistantClient.ClaudeDesktop;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or
                System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                messages.Add($"{assistant.Name}: {WindowPathText.Scrub(exception.Message)}");
            }
        }
        if (!_disposed) StatusMessage = string.Join(Environment.NewLine, messages);
    }

    /// <summary>Opens the bundled plugin ZIP in the system's file manager.</summary>
    [RelayCommand]
    private async Task OpenPluginZipAsync()
    {
        if (_launcher is null || string.IsNullOrWhiteSpace(PluginZipPath)) return;
        await _launcher.LaunchAsync(new Uri(Path.GetDirectoryName(PluginZipPath)!)).ConfigureAwait(true);
    }

    /// <summary>Opens Claude's plugin page for a person to upload the ZIP.</summary>
    [RelayCommand]
    private async Task OpenClaudePluginsAsync()
    {
        if (_launcher is null) return;
        await _launcher.LaunchAsync(new Uri("https://claude.ai/settings/plugins")).ConfigureAwait(true);
    }
}
