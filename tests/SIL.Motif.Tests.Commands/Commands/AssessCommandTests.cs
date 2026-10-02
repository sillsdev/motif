using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
[Trait("MotifTestLevel", "System")]
public sealed class AssessCommandTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public void SupportedAssessmentRecordsRealTimingAndStatisticsWithOneInvocation()
    {
        using var fixture = new DeterministicAssessCommandTests(pristine);
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "m", "o", "t", "i", "f", "a", "b");
        var selection = new SelectionRequest(false, [], ["motifa", "motifb", "mofita"], false, null);

        var outcome = AssessCommand.Assess(new AssessRequest(cache.ProjectId.Path, selection), fixture.NewManagedRoot());

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal(3, response.Words.Count);
        Assert.All(response.Words, word => Assert.False(word.IsIncomplete));
        Assert.Equal(2, response.Words.Count(word => word.Outcome == "analysed"));
        Assert.Equal(1, response.Words.Count(word => word.Outcome == "no-analysis"));
        Assert.StartsWith("3 searches completed; 0 incomplete", response.SummaryMarkdown);
        Assert.Contains("0/0 approved readings matched", response.CorrectnessStatus);
        Assert.Equal(3, response.Measurements.Count);
        Assert.Single(response.Measurements.Select(item => item.InvocationId).Distinct());
        var repository = DeterministicAssessCommandTests.OpenRepository(cache.ProjectId.Path);
        foreach (var measurement in response.Measurements)
        {
            var record = repository.Get(measurement.AssessmentId);
            Assert.Equal(measurement.InvocationId, record.Invocation!.InvocationId);
            Assert.Null(record.SemanticDigest);
        }

        // Object self times nest without overlap, so with each word's own nanoseconds they never exceed it.
        var parseId = response.Measurements.Single(item => item.Kind == "ParseTime").AssessmentId;
        var timing = TimingCommand.Timing(new TimingRequest(cache.ProjectId.Path, parseId, By: "kind"));
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        var attribution = timing.Value!.Attribution;
        Assert.Equal(3, attribution.MeasuredWordCount);
        Assert.True(attribution.AttributedMs > 0);
        Assert.False(attribution.Overrun, $"objects overran their words by {attribution.OverrunMs} ms");
        Assert.Equal(1, timing.Value.Aggregates.Sum(row => row.ShareOfWordTime!.Value) +
            attribution.NotAttributedShare!.Value, precision: 9);
    }

}
