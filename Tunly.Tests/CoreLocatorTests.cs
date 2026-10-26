using Tunly.Core;
using Xunit;

namespace Tunly.Tests;

public class CoreLocatorTests
{
    [Fact]
    public void Finds_a_bundled_core_under_the_core_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "tunly-core-" + Guid.NewGuid().ToString("N"));
        var coreDirectory = Path.Combine(root, "core");
        Directory.CreateDirectory(coreDirectory);
        var executable = Path.Combine(coreDirectory, "sing-box.exe");
        File.WriteAllText(executable, "");

        try
        {
            var found = CoreLocator.LocateBundled(root);

            Assert.NotNull(found);
            Assert.Equal(Path.GetFullPath(executable), found!.ExecutablePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Returns_null_when_no_bundled_core_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), "tunly-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Assert.Null(CoreLocator.LocateBundled(root));
            Assert.Null(CoreLocator.LocateBundled(""));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
