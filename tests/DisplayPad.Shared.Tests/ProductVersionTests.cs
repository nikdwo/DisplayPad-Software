using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Shared.Tests;

public sealed class ProductVersionTests
{
    [Fact]
    public void ProductionAssemblyUsesAlphaOneVersion()
    {
        Assert.Equal("0.1.0-alpha.1", ProductVersion.FromAssembly(typeof(AppConfig).Assembly));
        Assert.Equal(new Version(0, 1, 0, 0), typeof(AppConfig).Assembly.GetName().Version);
    }
}
