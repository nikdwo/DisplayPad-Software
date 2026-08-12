using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

public interface IRemoteAgentClient
{
    void Configure(string host, int port, string token, string certificateFingerprint);
    Task<ExecuteResponse> ExecuteAsync(KeyAction action, CancellationToken cancellationToken = default);
    Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default);
}

public sealed class ActionDispatcher : IRemoteAgentClient, IDisposable
{
    private HttpClient? _http;
    private string _baseUrl = "";
    private string _token = "";
    private string _configurationKey = "";

    public void Configure(string host, int port, string token, string certificateFingerprint)
    {
        var fingerprint = NormalizeFingerprint(certificateFingerprint);
        var key = $"{host}|{port}|{token}|{fingerprint}";
        if (key == _configurationKey) return;

        _configurationKey = key;
        _baseUrl = $"https://{host}:{port}";
        _token = token;
        _http?.Dispose();
        _http = null;
        if (fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit)) return;

        var handler = new HttpClientHandler
        {
            UseProxy = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null) return false;
                var actual = SHA256.HashData(certificate.GetRawCertData());
                return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(fingerprint));
            }
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ExecuteResponse> ExecuteAsync(KeyAction action, CancellationToken cancellationToken = default)
    {
        if (_http is null)
            return new ExecuteResponse { Success = false, ErrorCode = OperationErrorCode.RemoteMissingFingerprint };
        try
        {
            using var request = CreateRequest(HttpMethod.Post, "/execute");
            request.Content = JsonContent.Create(new ExecuteRequest { Action = action });
            using var response = await _http.SendAsync(request, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<ExecuteResponse>(cancellationToken: cancellationToken);
            return result ?? new ExecuteResponse
            {
                Success = false,
                ErrorCode = OperationErrorCode.RemoteHttpError,
                ErrorParameters = new[] { ((int)response.StatusCode).ToString() }
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ExecuteResponse
            {
                Success = false,
                ErrorCode = OperationErrorCode.RemoteNetworkError,
                Error = ex.Message
            };
        }
    }

    public async Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default)
    {
        if (_http is null) return null;
        try
        {
            using var request = CreateRequest(HttpMethod.Get, "/ping");
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PingResponse>(cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, _baseUrl + path);
        request.Headers.Add("X-Auth-Token", _token);
        return request;
    }

    private static string NormalizeFingerprint(string? value) =>
        new((value ?? "").Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());

    public void Dispose() => _http?.Dispose();
}
