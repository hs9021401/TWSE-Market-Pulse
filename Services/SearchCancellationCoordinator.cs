namespace TwseWidget.Services;

/// <summary>
/// Owns the cancellation source for the latest debounced search request.
/// A cancelled request remains undisposed until its own async handler finishes.
/// </summary>
public sealed class SearchCancellationCoordinator : IDisposable
{
    private CancellationTokenSource? _current;

    public CancellationTokenSource Start(CancellationToken lifetimeToken)
    {
        CancelCurrent();

        var request = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
        _current = request;
        return request;
    }

    public bool IsCurrent(CancellationTokenSource request) =>
        ReferenceEquals(_current, request);

    public void Finish(CancellationTokenSource request)
    {
        if (ReferenceEquals(_current, request))
        {
            _current = null;
        }

        request.Dispose();
    }

    public void CancelCurrent()
    {
        var current = _current;
        _current = null;
        current?.Cancel();
    }

    public void Dispose()
    {
        CancelCurrent();
    }
}
