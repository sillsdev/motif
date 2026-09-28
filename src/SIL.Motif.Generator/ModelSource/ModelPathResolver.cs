using System.Reflection;

namespace SIL.Motif.Generator.ModelSource;

/// <summary>
/// Finds <c>MasterLCModel.xml</c> without requiring a liblcm source checkout.
/// <c>SIL.LCModel.csproj</c> packs the file into the NuGet package under
/// <c>contentFiles/</c>, but not in the conventional <c>contentFiles/{lang}/{tfm}/</c> layout NuGet
/// auto-flows into a <c>PackageReference</c> consumer's build output — so it has to be located
/// directly in the package cache rather than found alongside this assembly.
/// </summary>
/// <remarks>
/// The package root is captured from the same MSBuild property used during restore, so runtime
/// environment differences cannot redirect the generator to another cache. NuGet lower-cases package
/// ids when laying out its cache, which is why <see cref="PackageId"/> below is lower-case.
/// </remarks>
public static class ModelPathResolver
{
    private const string PackageId = "sil.lcmodel";

    /// <summary>
    /// Resolves the package-cache path, falling back to a liblcm checkout only if
    /// <paramref name="libLcmCheckoutRoot"/> (or the <c>MOTIF_LIBLCM_CHECKOUT</c> environment
    /// variable) is set and the fallback file actually exists there. The fallback is accepted, never
    /// required — nothing in this repository's CI sets it, so the package-cache path is what CI
    /// always exercises.
    /// </summary>
    /// <param name="packagesRootOverride">Overrides the NuGet package cache root. Exists so tests
    /// can exercise package lookup and the checkout fallback without mutating process-wide environment
    /// variables that other tests might read concurrently.</param>
    /// <param name="libLcmCheckoutRoot">Overrides the checkout-fallback root for the same reason.</param>
    public static ModelPathResult Resolve(string? packagesRootOverride = null, string? libLcmCheckoutRoot = null)
    {
        var version = ReadPinnedPackageVersion();

        var packagesRoot = packagesRootOverride ?? ReadNuGetPackageRoot();

        var packageCachePath = Path.Combine(packagesRoot, PackageId, version, "contentFiles", "MasterLCModel.xml");
        if (File.Exists(packageCachePath))
            return new ModelPathResult(packageCachePath, ModelPathSource.NuGetPackageCache);

        // Fallback only, never required; the path is ADR 0014's: liblcm/src/SIL.LCModel/MasterLCModel.xml.
        var checkoutRoot = libLcmCheckoutRoot ?? Environment.GetEnvironmentVariable("MOTIF_LIBLCM_CHECKOUT");
        string? checkoutPath = null;
        if (!string.IsNullOrWhiteSpace(checkoutRoot))
        {
            checkoutPath = Path.Combine(checkoutRoot, "src", "SIL.LCModel", "MasterLCModel.xml");
            if (File.Exists(checkoutPath))
                return new ModelPathResult(checkoutPath, ModelPathSource.LibLcmCheckout);
        }

        throw new GeneratorException(
            $"Could not find MasterLCModel.xml. Tried the NuGet package cache at '{packageCachePath}'" +
            (checkoutPath is null
                ? " and no MOTIF_LIBLCM_CHECKOUT fallback was set."
                : $" and the liblcm checkout fallback at '{checkoutPath}'.") +
            " Restore the SIL.LCModel package, or point MOTIF_LIBLCM_CHECKOUT at a liblcm checkout.");
    }

    /// <summary>
    /// Reads the SIL.LCModel version this assembly was built against back out of the
    /// <see cref="AssemblyMetadataAttribute"/> the csproj emits from its own
    /// <c>$(SilLCModelPackageVersion)</c> property — the single source of truth, so the version
    /// string is never duplicated as a separate literal in code
    /// (SIL.Motif.Generator.csproj).
    /// </summary>
    public static string ReadPinnedPackageVersion()
    {
        var attribute = typeof(ModelPathResolver).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "SilLCModelPackageVersion");

        return attribute?.Value
            ?? throw new GeneratorException(
                "SilLCModelPackageVersion assembly metadata is missing; check SIL.Motif.Generator.csproj.");
    }

    /// <summary>Returns the NuGet cache root MSBuild used when restoring this assembly.</summary>
    public static string ReadNuGetPackageRoot()
    {
        var attribute = typeof(ModelPathResolver).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NuGetPackageRoot");

        return attribute?.Value
            ?? throw new GeneratorException(
                "NuGetPackageRoot assembly metadata is missing; check SIL.Motif.Generator.csproj.");
    }
}
