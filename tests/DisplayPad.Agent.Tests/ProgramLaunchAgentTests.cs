using System.Net;
using System.Net.Http.Json;
using System.Text;
using DisplayPad.Host.Services;
using DisplayPad.Shared.Dto;
using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Agent.Tests;

public sealed class ProgramLaunchAgentTests
{
    [Theory]
    [InlineData("{\"status\":\"ok\"}", 200, OperationErrorCode.AgentProgramUpdateRequired)]
    [InlineData("{\"supportsProgramLaunch\":false}", 200, OperationErrorCode.AgentProgramUpdateRequired)]
    [InlineData("{\"supportsProgramLaunch\":true}", 200, OperationErrorCode.None)]
    [InlineData("not json", 200, OperationErrorCode.RemoteNetworkError)]
    [InlineData("null", 200, OperationErrorCode.RemoteNetworkError)]
    [InlineData("{}", 401, OperationErrorCode.RemoteHttpError)]
    public async Task CapabilityCheckProtectsOlderAgentsWithoutBlockingExistingCommands(
        string pingJson, int pingStatus, OperationErrorCode expected)
    {
        int pingCalls = 0, executeCalls = 0;
        KeyAction? received = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal("https", request.RequestUri!.Scheme);
            Assert.Equal("test-token", Assert.Single(request.Headers.GetValues("X-Auth-Token")));
            if (request.RequestUri.AbsolutePath == "/ping")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                pingCalls++;
                return new HttpResponseMessage((HttpStatusCode)pingStatus)
                    { Content = new StringContent(pingJson, Encoding.UTF8, "application/json") };
            }
            Assert.Equal("/execute", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            executeCalls++;
            received = (await request.Content!.ReadFromJsonAsync<ExecuteRequest>())!.Action;
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new ExecuteResponse { Success = true }) };
        }));
        using var client = new ActionDispatcher(http, "https://test.invalid", "test-token");
        var result = await client.ExecuteAsync(new KeyAction
        {
            Type = KeyActionType.LaunchProgram, ProgramPath = "C:\\My App\\app.exe",
            ProgramArguments = "--name \"two words\"", WorkingDirectory = "D:\\Work"
        });
        Assert.Equal(expected, result.ErrorCode);
        Assert.Equal(expected == OperationErrorCode.None, result.Success);
        Assert.Equal(1, pingCalls);
        Assert.Equal(expected == OperationErrorCode.None ? 1 : 0, executeCalls);
        if (result.Success)
        {
            Assert.Equal(KeyActionType.LaunchProgram, received!.Type);
            Assert.Equal("C:\\My App\\app.exe", received.ProgramPath);
            Assert.Equal("--name \"two words\"", received.ProgramArguments);
            Assert.Equal("D:\\Work", received.WorkingDirectory);
        }

        Assert.True((await client.ExecuteAsync(new KeyAction
            { Type = KeyActionType.Command, CommandLine = "echo unchanged" })).Success);
        Assert.Equal(1, pingCalls);
        Assert.Equal(KeyActionType.Command, received!.Type);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
