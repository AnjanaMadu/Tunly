using Tunly.Core.Models;
using Xunit;

namespace Tunly.Tests;

public class CoreVersionTests
{
    [Theory]
    [InlineData("sing-box version 1.14.4", 1, 14, 4)]
    [InlineData("sing-box version 1.12.0-beta.3", 1, 12, 0)]
    [InlineData("Environment: go1.26\r\nsing-box version 1.13.2", 1, 13, 2)]
    [InlineData("1.9.7", 1, 9, 7)]
    public void Parses_version_output(string text, int major, int minor, int patch)
    {
        var version = CoreVersion.TryParse(text);
        Assert.NotNull(version);
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no version here")]
    [InlineData("sing-box 1")]
    public void Rejects_invalid_output(string text)
    {
        Assert.Null(CoreVersion.TryParse(text));
    }

    [Theory]
    [InlineData(1, 14, 12, true)]
    [InlineData(1, 12, 12, true)]
    [InlineData(1, 11, 12, false)]
    [InlineData(2, 0, 12, true)]
    public void AtLeast_compares_correctly(int major, int minor, int requiredMinor, bool expected)
    {
        var version = new CoreVersion(major, minor, 0, "");
        Assert.Equal(expected, version.AtLeast(1, requiredMinor));
    }
}
