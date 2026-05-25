namespace RfidGateway.Services;

public sealed class ReaderStatusService
{
    private volatile bool _isConnected;

    public bool IsConnected => _isConnected;

    public void SetConnected(bool connected) => _isConnected = connected;
}
