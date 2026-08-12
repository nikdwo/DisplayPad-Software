using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DisplayPad.Agent;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Agent.Tests;

public sealed class AgentEndpointTests
{
    [Fact]
    public async Task EndpointsEnforceAuthenticationActionAllowlistRateLimitAndBodyLimit()
    {
        using var certificate = CreateCertificate();
        var port = GetAvailablePort();
        var config = new AgentConfig { BindAddress = "127.0.0.1", Port = port, Token = "correct-token" };
        await using var app = AgentWebApplication.Build(config, certificate);
        await app.StartAsync();
        try
        {
            var baseAddress = new Uri($"https://127.0.0.1:{port}");
            string? servedFingerprint = null;
            using var handler = new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, servedCertificate, _, _) =>
                {
                    servedFingerprint = servedCertificate is null
                        ? null
                        : Convert.ToHexString(SHA256.HashData(servedCertificate.GetRawCertData()));
                    return true;
                }
            };
            using var client = new HttpClient(handler) { BaseAddress = baseAddress };

            using (var initialPing = new HttpRequestMessage(HttpMethod.Get, "/ping"))
            {
                initialPing.Headers.Add("X-Auth-Token", config.Token);
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(initialPing)).StatusCode);
            }
            Assert.NotNull(servedFingerprint);

            using var pinnedClient = new ActionDispatcher();
            pinnedClient.Configure("127.0.0.1", port, config.Token, servedFingerprint!);
            Assert.NotNull(await pinnedClient.PingAsync());
            pinnedClient.Configure("127.0.0.1", port, config.Token, new string('0', 64));
            Assert.Null(await pinnedClient.PingAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ping")).StatusCode);

            using var authenticatedPing = new HttpRequestMessage(HttpMethod.Get, "/ping");
            authenticatedPing.Headers.Add("X-Auth-Token", config.Token);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(authenticatedPing)).StatusCode);

            using var unsupported = new HttpRequestMessage(HttpMethod.Post, "/execute")
            {
                Content = JsonContent.Create(new ExecuteRequest { Action = new KeyAction { Type = KeyActionType.Obs } })
            };
            unsupported.Headers.Add("X-Auth-Token", config.Token);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(unsupported)).StatusCode);

            using var oversized = new HttpRequestMessage(HttpMethod.Post, "/execute")
            {
                Content = new StringContent("\"" + new string('x', (int)AgentWebApplication.MaximumBodyBytes + 1) + "\"",
                    Encoding.UTF8, "application/json")
            };
            oversized.Headers.Add("X-Auth-Token", config.Token);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.SendAsync(oversized)).StatusCode);

            for (var attempt = 0; attempt < 5; attempt++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ping")).StatusCode);
            Assert.Equal((HttpStatusCode)429, (await client.GetAsync("/ping")).StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(generated.Export(X509ContentType.Pfx));
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
