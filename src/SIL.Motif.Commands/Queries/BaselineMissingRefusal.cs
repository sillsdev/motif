using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

internal static class BaselineMissingRefusal
{
    internal static Refusal ForProject(string projectPath) => new(
        "baseline.missing",
        FailureReason.NotFound,
        "A Baseline is required to check or read grammar findings. Run: " +
        "motif baseline capture --project <fwdata>, then try again.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = projectPath });
}
