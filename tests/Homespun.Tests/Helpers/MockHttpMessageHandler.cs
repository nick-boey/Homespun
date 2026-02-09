using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Homespun.Tests.Helpers;

/// <summary>
/// A mock HttpMessageHandler for testing Client HTTP API services.
/// Allows configuring predefined responses for specific request patterns.
/// </summary>
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private HttpResponseMessage _defaultResponse = new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new object())
    };

    /// <summary>
    /// Configure a response for GET requests matching the given URL pattern.
    /// </summary>
    public MockHttpMessageHandler RespondWith<T>(string urlContains, T responseBody)
    {
        _responses[urlContains] = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(responseBody)
        };
        return this;
    }

    /// <summary>
    /// Configure a response that returns NotFound for requests matching the given URL pattern.
    /// </summary>
    public MockHttpMessageHandler RespondNotFound(string urlContains)
    {
        _responses[urlContains] = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        return this;
    }

    /// <summary>
    /// Configure a response for any HTTP method matching the given URL pattern.
    /// </summary>
    public MockHttpMessageHandler RespondWithStatus(string urlContains, HttpStatusCode statusCode)
    {
        _responses[urlContains] = _ => new HttpResponseMessage(statusCode);
        return this;
    }

    /// <summary>
    /// Set the default response for unmatched requests.
    /// </summary>
    public MockHttpMessageHandler WithDefaultResponse<T>(T responseBody)
    {
        _defaultResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(responseBody)
        };
        return this;
    }

    /// <summary>
    /// Creates an HttpClient backed by this mock handler.
    /// </summary>
    public HttpClient CreateClient()
    {
        return new HttpClient(this) { BaseAddress = new Uri("http://localhost/") };
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestUrl = request.RequestUri?.ToString() ?? "";

        // Match longest pattern first to ensure specific URLs take priority over generic ones
        foreach (var (pattern, responseFactory) in _responses.OrderByDescending(r => r.Key.Length))
        {
            if (requestUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(responseFactory(request));
            }
        }

        return Task.FromResult(_defaultResponse);
    }
}
