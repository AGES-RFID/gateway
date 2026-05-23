namespace RfidGateway.Services;

public sealed class ReaderStatusService
{
    private readonly object _sync = new();
    private bool _isConnected;

    public bool IsConnected
    {
        get { lock (_sync) return _isConnected; }
    }

    public void SetConnected(bool connected)
    {
        lock (_sync) _isConnected = connected;
    }
}
