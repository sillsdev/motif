using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Catalog;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ICommandClientCatalogParityTests
{
    private static readonly IReadOnlyDictionary<string, string> CataloguedCommands =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ICommandClient.CaptureBaselineAsync)] = "baseline capture",
            [nameof(ICommandClient.ListTextsAsync)] = "texts list",
            [nameof(ICommandClient.AssessAsync)] = "assess",
            [nameof(ICommandClient.StatsAsync)] = "stats",
            [nameof(ICommandClient.HandoffAsync)] = "handoff",
            [nameof(ICommandClient.OverviewAsync)] = "overview",
            [nameof(ICommandClient.TimingAsync)] = "timing",
            [nameof(ICommandClient.CheckGrammarAsync)] = "grammar check",
            [nameof(ICommandClient.ReadDefaultSelectionAsync)] = "selection show",
            [nameof(ICommandClient.ReadRetirementReviewAsync)] = "parsimony retirement-review",
            [nameof(ICommandClient.SetDefaultSelectionAsync)] = "selection set-default",
            [nameof(ICommandClient.SetSelectionLimitsAsync)] = "selection set-limits",
            [nameof(ICommandClient.SkipSetupAsync)] = "setup skip",
            [nameof(ICommandClient.ShowConfigAsync)] = "config show",
            [nameof(ICommandClient.MeasurePendingAsync)] = "trial --pending",
            [nameof(ICommandClient.ApplyPendingAsync)] = "apply --all-pending",
            [nameof(ICommandClient.LoadPendingChangesAsync)] = "pending-changes",
            [nameof(ICommandClient.PutPendingChangeAsync)] = "put-pending-change",
            [nameof(ICommandClient.RemovePendingChangeAsync)] = "remove-pending-change",
            [nameof(ICommandClient.RemoveAnalysisAsync)] = "remove-analysis",
            [nameof(ICommandClient.AcceptNewSetAsync)] = "accept-new-set",
            [nameof(ICommandClient.RecheckPendingChangesAsync)] = "recheck-pending-changes",
            [nameof(ICommandClient.ReconfirmPendingChangeAsync)] = "reconfirm-pending-change",
            [nameof(ICommandClient.ReadWordStateAsync)] = "word read-state",
            [nameof(ICommandClient.DeleteRefusedStoreAsync)] = "store delete-refused",
            [nameof(ICommandClient.TraceWordAsync)] = "trace",
            [nameof(ICommandClient.ReadWordContextAsync)] = "word-context",
            [nameof(ICommandClient.InspectAsync)] = "inspect",
            [nameof(ICommandClient.ReadParsimonyViewAsync)] = "parsimony view",
            [nameof(ICommandClient.ListNotebookRecordTypesAsync)] = "parsimony record-types",
            [nameof(ICommandClient.RecordParsimonyDispositionAsync)] = "parsimony dispose",
            [nameof(ICommandClient.RetractParsimonyDispositionAsync)] = "parsimony retract",
        };

    // A store-writing client method must map to a catalogued CLI verb.
    private static readonly HashSet<string> ReadsThatNeverWriteTheStore = new(StringComparer.Ordinal)
    {
        nameof(ICommandClient.ListKnownProjectsAsync),
        nameof(ICommandClient.GetCurrentBaselineAsync),
        nameof(ICommandClient.ReadCurrentEvidenceAsync),
        nameof(ICommandClient.GetProjectHistoryAsync),
        nameof(ICommandClient.ReadStoredGrammarCheckAsync),
        nameof(ICommandClient.OpenSelectionReaderAsync),
        nameof(ICommandClient.ReadParserStepRateAsync),
        nameof(ICommandClient.RestoreBackupAsync),
        nameof(ICommandClient.ReadLatestParsimonyReportAsync),
        nameof(ICommandClient.ReadParsimonyReportAsync),
    };

    [Fact]
    public void EveryClientMethodMapsToACataloguedCommandOrAStoreFreeRead()
    {
        var methods = typeof(ICommandClient).GetMethods()
            .Where(method => method.Name != nameof(ICommandClient.BeginUsageAction))
            .ToDictionary(method => method.Name, StringComparer.Ordinal);
        var accountedFor = CataloguedCommands.Keys.Concat(ReadsThatNeverWriteTheStore).ToArray();

        Assert.Equal(methods.Keys.Order(StringComparer.Ordinal), accountedFor.Order(StringComparer.Ordinal));
        Assert.Empty(CataloguedCommands.Keys.Intersect(ReadsThatNeverWriteTheStore, StringComparer.Ordinal));

        foreach (var (methodName, commandName) in CataloguedCommands)
        {
            var method = methods[methodName];
            var descriptor = Assert.Single(CommandCatalog.All, command => command.Name == commandName);
            var outcome = method.ReturnType.GetGenericArguments().Single();

            Assert.Equal(descriptor.RequestType, method.GetParameters()[0].ParameterType);
            Assert.Equal(descriptor.ResponseType, outcome.GetGenericArguments().Single());
        }
    }
}
