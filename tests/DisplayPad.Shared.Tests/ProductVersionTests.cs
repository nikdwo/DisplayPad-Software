using DisplayPad.Shared.Models;
using Xunit;

namespace DisplayPad.Shared.Tests;

public sealed class ProductVersionTests
{
    [Fact]
    public void ProductAndAssemblyVersionsAreConsistent()
    {
        var assembly = typeof(AppConfig).Assembly;
        var productVersion = ProductVersion.FromAssembly(assembly);
        var semanticCore = Version.Parse(productVersion.Split('-', 2)[0]);

        Assert.Matches(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$", productVersion);
        Assert.Equal(new Version(semanticCore.Major, semanticCore.Minor, semanticCore.Build, 0),
            assembly.GetName().Version);
    }
}
