using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;

namespace SIL.Motif.Help;

internal static class ManifestResourceLookup
{
    internal static IEnumerable<(string PhysicalName, string LogicalName)> Enumerate(Assembly assembly) =>
        assembly.GetManifestResourceNames()
            .Select(name => (name, NormalizeName(name)));

    internal static Stream? GetStream(Assembly assembly, string logicalName)
    {
        var physicalName = FindPhysicalName(assembly.GetManifestResourceNames(), logicalName);
        return physicalName is null ? null : assembly.GetManifestResourceStream(physicalName);
    }

    internal static string? FindPhysicalName(IEnumerable<string> resourceNames, string logicalName) =>
        resourceNames.FirstOrDefault(name =>
            string.Equals(NormalizeName(name), logicalName, StringComparison.Ordinal));

    internal static string NormalizeName(string resourceName) => resourceName.Replace('\\', '/');

    internal static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\n");
}
