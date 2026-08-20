using System.Net.Http.Json;

namespace indian_ticketing.AI.Planning;

/// <summary>All Ollama-specific HTTP details live here — browser automation classes never call HttpClient directly for model calls.</summary>
public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private readonly HttpClient _http;

    public OllamaClient(string baseUrl, int timeoutSeconds)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)) };
    }

    public async Task<string> GenerateAsync(string model, string prompt, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0.1 },
        };

        using var response = await _http.PostAsJsonAsync("/api/generate", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<GenerateResponse>(cancellationToken: cancellationToken);
        return body?.Response ?? "";
    }

    private sealed class GenerateResponse
    {
        public string? Response { get; set; }
    }

    public void Dispose() => _http.Dispose();
}
