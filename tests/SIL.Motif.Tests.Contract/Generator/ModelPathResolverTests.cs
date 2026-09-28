using SIL.Motif.Generator;
using SIL.Motif.Generator.ModelSource;
using Xunit;

namespace SIL.Motif.Tests.Generator;

[CollectionDefinition("Model path resolver environment", DisableParallelization = true)]
public sealed class ModelPathResolverEnvironmentCollection
{
}

/// <summary>
/// <c>MasterLCModel.xml</c> is readable from the restored NuGet package with no liblcm source checkout.
/// </summary>
[Collection("Model path resolver environment")]
public class ModelPathResolverTests
{
    [Fact]
    public void Resolve_FindsRealFile_ViaNuGetPackageCache()
    {
        var result = ModelPathResolver.Resolve();

        Assert.Equal(ModelPathSource.NuGetPackageCache, result.Source);
        Assert.True(File.Exists(result.Path), $"Expected '{result.Path}' to exist.");
        Assert.Contains("contentFiles", result.Path);
        Assert.EndsWith("MasterLCModel.xml", result.Path);
    }

    [Fact]
    public void Resolve_UsesThePackageRootCapturedAtBuild_WhenRuntimeEnvironmentDiffers()
    {
        var fakePackagesRoot = Path.Combine(Path.GetTempPath(), "motif-tests", Guid.NewGuid().ToString("N"));
        var fakeModelPath = Path.Combine(fakePackagesRoot, "sil.lcmodel", ModelPathResolver.ReadPinnedPackageVersion(), "contentFiles", "MasterLCModel.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(fakeModelPath)!);
        File.WriteAllText(fakeModelPath,
            "<EntireModel version=\"7000072\"><CellarModule><class id=\"ChkRef\"><props><basic id=\"KeyWord\" sig=\"Unicode\" /></props></class></CellarModule></EntireModel>");

        var originalPackagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", fakePackagesRoot);

            var loaded = MotifModelLoader.Load();

            Assert.Equal(898, loaded.Rows.Count);
            Assert.Equal("7000072", loaded.Model.Version);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", originalPackagesRoot);
            Directory.Delete(fakePackagesRoot, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UsesExplicitPackagesRootOverride()
    {
        // The override exercises the package layout without mutating process-wide environment variables.
        var realRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(
            ModelPathResolver.Resolve().Path)))!; // .../sil.lcmodel/{version}/contentFiles/MasterLCModel.xml -> .../packages
        var packagesRoot = Path.GetDirectoryName(realRoot)!;

        var result = ModelPathResolver.Resolve(packagesRootOverride: packagesRoot);

        Assert.Equal(ModelPathSource.NuGetPackageCache, result.Source);
        Assert.True(File.Exists(result.Path));
    }

    [Fact]
    public void Resolve_FallsBackToLibLcmCheckout_WhenPackageCacheMissingButCheckoutPresent()
    {
        var fakePackagesRoot = Path.Combine(Path.GetTempPath(), "motif-tests", Guid.NewGuid().ToString("N"), "empty-packages");
        Directory.CreateDirectory(fakePackagesRoot);

        var fakeCheckoutRoot = Path.Combine(Path.GetTempPath(), "motif-tests", Guid.NewGuid().ToString("N"), "liblcm-checkout");
        var fakeModelDir = Path.Combine(fakeCheckoutRoot, "src", "SIL.LCModel");
        Directory.CreateDirectory(fakeModelDir);
        File.WriteAllText(Path.Combine(fakeModelDir, "MasterLCModel.xml"), "<EntireModel version=\"1\"/>");

        try
        {
            var result = ModelPathResolver.Resolve(packagesRootOverride: fakePackagesRoot, libLcmCheckoutRoot: fakeCheckoutRoot);

            Assert.Equal(ModelPathSource.LibLcmCheckout, result.Source);
            Assert.True(File.Exists(result.Path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fakePackagesRoot)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(fakeCheckoutRoot)!, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ThrowsNamingBothAttemptedPaths_WhenNeitherExists()
    {
        var fakePackagesRoot = Path.Combine(Path.GetTempPath(), "motif-tests", Guid.NewGuid().ToString("N"), "empty-packages");
        Directory.CreateDirectory(fakePackagesRoot);

        try
        {
            var ex = Assert.Throws<GeneratorException>(() =>
                ModelPathResolver.Resolve(packagesRootOverride: fakePackagesRoot, libLcmCheckoutRoot: null));

            Assert.Contains("MasterLCModel.xml", ex.Message);
            Assert.Contains(fakePackagesRoot, ex.Message);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fakePackagesRoot)!, recursive: true);
        }
    }

    [Fact]
    public void ReadPinnedPackageVersion_MatchesTheCsprojProperty()
    {
        // Must match SilVersions.props, the shared version AssemblyMetadata carries in.
        Assert.Equal("11.0.0-beta0182", ModelPathResolver.ReadPinnedPackageVersion());
    }
}
