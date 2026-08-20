namespace indian_ticketing.AI.Planning;

public interface IOllamaClient
{
    Task<string> GenerateAsync(string model, string prompt, CancellationToken cancellationToken = default);
}
