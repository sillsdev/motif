namespace SIL.Motif.App.Services;

/// <summary>The state of the ChatGPT tunnel owned by this window.</summary>
public enum TunnelStatus { NotConnected, Connecting, Connected, Error }

/// <summary>Starts and stops the tunnel for this desktop session.</summary>
public interface ITunnelClient : IDisposable
{
    /// <summary>The current connection state.</summary>
    TunnelStatus Status { get; }
    /// <summary>Raised when the connection state changes.</summary>
    event Action<TunnelStatus>? StatusChanged;
    /// <summary>Starts the tunnel using the runtime key and tunnel identifier.</summary>
    Task ConnectAsync(string key, string tunnelId);
    /// <summary>Stops the owned tunnel and its children.</summary>
    void Disconnect();
}
