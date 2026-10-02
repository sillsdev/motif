using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TraceFailureSentenceTests
{
    [Fact]
    public void EveryPinnedPanGlossReasonHasOneWindowSentence()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "TestFixtures", "pangloss-0.6.0-trace-reasons.json")));
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "pangloss-release.json")))
            repository = repository.Parent;
        Assert.NotNull(repository);
        using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository!.FullName, "pangloss-release.json")));
        Assert.Equal(pin.RootElement.GetProperty("version").GetString(), catalog.RootElement.GetProperty("version").GetString());
        var codes = catalog.RootElement.GetProperty("codes").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(23, codes.Length);
        Assert.Equal(codes.Order(), TraceFailureSentences.Catalog.Keys.Order());
        Assert.All(codes, code => Assert.DoesNotContain("not recorded", TraceFailureSentences.Explain(code)));
    }

    [Fact]
    public void RefusalNamesOnlyRecordedRuleAndFeatureText()
    {
        Assert.Equal("This attempt needs an inflection feature the word doesn't have.",
            TraceFailureSentences.Explain("RequiredSyntacticFeatureStruct"));
        Assert.Equal("ja- needs an inflection feature the word doesn't have. Required: Polarity: negative.",
            TraceFailureSentences.Explain("RequiredSyntacticFeatureStruct", "ja-", "Polarity: negative"));
        Assert.DoesNotContain("17", TraceFailureSentences.Explain("RequiredSyntacticFeatureStruct", null, "{\"17\":0}"));
        Assert.Equal("PanGloss didn't record why.", TraceFailureSentences.Explain(null));
        Assert.Equal("PanGloss recorded an unfamiliar reason: FutureCode.", TraceFailureSentences.Explain("FutureCode"));
    }

    [Fact]
    public void BuildingStoryKeepsForwardEventsAndNeverIncludesAnEarlierSibling()
    {
        var reverse = new TraceStep("MorphologicalRuleAnalysis", "suffix", null, "stem", null, []) { StepId = "0.1" };
        var forward = new TraceStep("MorphologicalRuleSynthesis", "suffix", "stem", "stems", null, []) { StepId = "0.1.0" };
        var result = new TraceStep("Successful", null, null, "stems", null, []) { StepId = "0.1.0.0" };
        Assert.Equal([reverse, forward, result], TraceBuildingStory.Steps([reverse, forward, result]));
        Assert.Equal([result], TraceBuildingStory.Steps([result]));
    }
}
