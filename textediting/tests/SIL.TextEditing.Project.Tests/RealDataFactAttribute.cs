using System;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>A fact that runs only when TEXTEDITING_EXPORT_FWDATA names a project file, and is reported as skipped, with that reason, when it does not.</summary>
    public sealed class RealDataFactAttribute : FactAttribute
    {
        public const string Variable = "TEXTEDITING_EXPORT_FWDATA";

        public RealDataFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
                Skip = "Not opted in: " + Variable + " is not set to a .fwdata path.";
        }
    }
}
