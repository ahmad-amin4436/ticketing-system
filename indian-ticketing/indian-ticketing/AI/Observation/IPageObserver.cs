namespace indian_ticketing.AI.Observation;

public interface IPageObserver
{
    Task<PageState> ObserveAsync(CancellationToken cancellationToken = default);
}
