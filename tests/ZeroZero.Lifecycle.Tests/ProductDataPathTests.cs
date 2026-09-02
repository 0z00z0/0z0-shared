using Xunit;
using ZeroZero.Lifecycle;

namespace ZeroZero.Lifecycle.Tests;

/// <summary>The folder every other per-user file hangs off. Each test uses a product name of its
/// own under the real roaming folder and removes it afterwards.</summary>
public class ProductDataPathTests
{
    private static string DisposableProduct() => "ZeroZero.Lifecycle.Tests." + Guid.NewGuid().ToString("N");

    [Fact]
    public void TheRootIsTheRoamingFolderUnderTheProductAndExistsOnReturn()
    {
        string product = DisposableProduct();
        try
        {
            string root = ProductDataPath.Root(product);

            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), product), root);
            Assert.True(Directory.Exists(root));
        }
        finally
        {
            Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), product), recursive: true);
        }
    }

    [Fact]
    public void AFileUnderTheRootIsComposedAndNotCreated()
    {
        string product = DisposableProduct();
        try
        {
            string path = ProductDataPath.Under(product, Path.Combine("logs", "app.log"));

            Assert.Equal(Path.Combine(ProductDataPath.Root(product), "logs", "app.log"), path);
            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        }
        finally
        {
            Directory.Delete(ProductDataPath.Root(product), recursive: true);
        }
    }

    [Fact]
    public void ARootedPathIsRefusedRatherThanLeavingTheFolder()
    {
        string elsewhere = Path.Combine(Path.GetTempPath(), "elsewhere.txt");

        var exception = Assert.Throws<ArgumentException>(() => ProductDataPath.Under("ZeroZero.Lifecycle.Tests.Unused", elsewhere));

        Assert.Equal("relativePath", exception.ParamName);
    }

    [Fact]
    public void ABlankProductNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ProductDataPath.Root(" "));
        Assert.Throws<ArgumentException>(() => ProductDataPath.Under("ZeroZero.Lifecycle.Tests.Unused", ""));
    }
}
