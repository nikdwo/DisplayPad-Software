using DisplayPad.Agent;
using Xunit;

namespace DisplayPad.Agent.Tests;

public sealed class FailedAuthenticationLimiterTests
{
    [Fact]
    public void BlocksOnlyFailingSourceAndSuccessClearsFailures()
    {
        var limiter = new FailedAuthenticationLimiter(2, TimeSpan.FromMinutes(1));
        limiter.RecordFailure("one");
        limiter.RecordFailure("one");

        Assert.True(limiter.IsBlocked("one"));
        Assert.False(limiter.IsBlocked("two"));

        limiter.RecordSuccess("one");
        Assert.False(limiter.IsBlocked("one"));
    }
}
