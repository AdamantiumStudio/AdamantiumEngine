using System.IO;
using NUnit.Framework;

namespace Adamantium.ImagingTests;

/// <summary>The repository's Tests/TestAssets folder, found upward from where the tests run.</summary>
public static class TestAssets
{
    /// <summary>The full path of <paramref name="relative"/> inside Tests/TestAssets.</summary>
    public static string Path(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "Tests", "TestAssets");
            if (Directory.Exists(candidate))
            {
                return System.IO.Path.Combine(candidate, relative);
            }
        }

        throw new DirectoryNotFoundException("Tests/TestAssets");
    }
}
