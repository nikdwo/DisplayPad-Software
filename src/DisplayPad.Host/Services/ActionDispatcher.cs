using System.Net.Http;
using System.Net.Http.Json;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;

namespace DisplayPad.Host.Services;

/// <summary>Sendet Aktionen an den Agent auf dem Zweitrechner.</summary>
public class ActionDispatcher
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private string _baseUrl = "";
    private string _token = "";

    public void Configure(string host, int port, string token)
    {
        _baseUrl = $"http://{host}:{port}";
        _token = token;
    }

    public async Task<ExecuteResponse> ExecuteAsync(KeyAction action)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/execute")
            {
                Content = JsonContent.Create(new ExecuteRequest { Action = action })
            };
            request.Headers.Add("X-Auth-Token", _token);
            var response = await _http.SendAsync(request);
            var result = await response.Content.ReadFromJsonAsync<ExecuteResponse>();
            return result ?? new ExecuteResponse { Success = false, Error = "Leere Antwort vom Agent" };
        }
        catch (Exception ex)
        {
            return new ExecuteResponse { Success = false, Error = ex.Message };
        }
    }

    public async Task<PingResponse?> PingAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/ping");
            request.Headers.Add("X-Auth-Token", _token);
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadFromJsonAsync<PingResponse>();
        }
        catch
        {
            return null;
        }
    }
}
