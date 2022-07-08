using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Signal.Beacon.Application.Auth;
using Signal.Beacon.Application.Signal.Client.Station;

namespace Signal.Beacon.Application.Signal.Client;

internal class SignalcoClient : ISignalClient, ISignalcoClientAuthFlow
{
    private const string ApiUrl = "https://api.signalco.io/api";

    private static readonly string ApiStationRefreshTokenUrl = "/station/refresh-token";

    private readonly ILogger<SignalcoClient> logger;
    private readonly HttpClient client = new();
    private AuthToken? token;
    private readonly SemaphoreSlim renewLock = new(1, 1);
    private readonly AsyncCircuitBreakerPolicy circuitBreakerPolicy;

    public event EventHandler<AuthToken?>? OnTokenRefreshed;

    private static readonly JsonSerializerOptions caseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    public SignalcoClient(ILogger<SignalcoClient> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>(ex =>
                ex.StatusCode.HasValue && ((int)ex.StatusCode > 500 || ex.StatusCode == HttpStatusCode.RequestTimeout))
            .AdvancedCircuitBreakerAsync(
                failureThreshold: 0.5,
                samplingDuration: TimeSpan.FromSeconds(10),
                minimumThroughput: 4,
                durationOfBreak: TimeSpan.FromSeconds(30));
    }


    public void AssignToken(AuthToken newToken)
    {
        token = newToken;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token.AccessToken);

        logger.LogDebug("Token successfully assigned. Expires on: {TokenExpire}", token.Expire);
    }

    public async Task<AuthToken?> GetTokenAsync(CancellationToken cancellationToken)
    {
        await RenewTokenIfExpiredAsync(cancellationToken);
        return token;
    }

    private async Task RenewTokenIfExpiredAsync(CancellationToken cancellationToken)
    {
        // Can't renew unassigned token (used for unauthenticated requests)
        if (token == null)
            return;

        // Not expired
        if (DateTime.UtcNow < token.Expire)
            return;

        // Lock
        try
        {
            await renewLock.WaitAsync(cancellationToken);

            // Request new token from Signal API
            var response = await PostAsJsonAsync<SignalcoStationRefreshTokenRequestDto, SignalcoStationRefreshTokenResponseDto>(
                ApiStationRefreshTokenUrl,
                new SignalcoStationRefreshTokenRequestDto(token.RefreshToken),
                cancellationToken,
                false);
            if (response == null)
                throw new Exception("Failed to renew token - got null response.");

            // Check if someone else assigned new token already
            if (DateTime.UtcNow < token.Expire)
                return;

            // Assign new token
            AssignToken(new AuthToken(response.AccessToken, token.RefreshToken, response.Expire));
            logger.LogDebug("Token successfully refreshed. Expires on: {TokenExpire}", token.Expire);
        }
        finally
        {
            renewLock.Release();
        }

        // Notify token was refreshed so it can be persisted
        OnTokenRefreshed?.Invoke(this, await GetTokenAsync(cancellationToken));
    }

    public async Task PostAsJsonAsync<T>(string url, T data, CancellationToken cancellationToken)
    {
        await HandleHttpErrorsAsync<T>(async () =>
        {
            await RenewTokenIfExpiredAsync(cancellationToken);

            using var response = await circuitBreakerPolicy.ExecuteAsync(async () =>
                await client.PostAsJsonAsync($"{ApiUrl}{url}", data, cancellationToken));
            if (!response.IsSuccessStatusCode)
                throw new Exception(
                    $"Signal API POST {ApiUrl}{url} failed. Reason: {await response.Content.ReadAsStringAsync(cancellationToken)} ({response.StatusCode})");

            return default;
        });
    }

    public async Task<TResponse?> PostAsJsonAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken cancellationToken, bool renewTokenIfExpired = true)
    {
        return await HandleHttpErrorsAsync(async () =>
        {
            if (renewTokenIfExpired)
                await RenewTokenIfExpiredAsync(cancellationToken);

            using var response = await circuitBreakerPolicy.ExecuteAsync(async () =>
                await client.PostAsJsonAsync($"{ApiUrl}{url}", data, cancellationToken));
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                    throw new Exception(
                        $"API returned NOCONTENT but we expected response of type {typeof(TResponse).FullName}");

                try
                {
                    var responseData = await response.Content.ReadFromJsonAsync<TResponse>(
                        caseInsensitiveOptions,
                        cancellationToken);
                    return responseData;
                }
                catch (JsonException ex)
                {
                    var responseDataString = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogTrace(ex, "Failed to read response JSON.");
                    logger.LogDebug("Reading response JSON failed. Raw: {DataString}", responseDataString);
                    throw;
                }
            }

            var responseContent = await GetResponseContentStringAsync(response, cancellationToken);
            throw new Exception(
                $"Signal API POST {ApiUrl}{url} failed. Reason: {responseContent} ({response.StatusCode})");
        });
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        return await HandleHttpErrorsAsync(async () =>
        {
            await RenewTokenIfExpiredAsync(cancellationToken);

            return await circuitBreakerPolicy.ExecuteAsync(async () =>
                await client.GetFromJsonAsync<T>(
                    $"{ApiUrl}{url}",
                    caseInsensitiveOptions,
                    cancellationToken));
        });
    }

    private async Task<T?> HandleHttpErrorsAsync<T>(Func<Task<T?>> func)
    {
        try
        {
            return await func();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            logger.LogTrace(ex, "API unavailable");
            logger.LogWarning("API unavailable");
            return default;
        }
    }

    private async Task<string> GetResponseContentStringAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(responseString) ? "No content." : responseString;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to read API response content.");
            return "Failed to read API response content.";
        }
    }
}