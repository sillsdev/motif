using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Parser;

/// <summary>
/// <see cref="PanGlossTracer"/> against the real <c>pangloss</c> executable: the same grammar and word
/// PanGloss's own <c>trace_render.rs</c> golden test uses, so a real trace's shape can be checked against a
/// tree this suite also has a fixture capture of (<c>PanGlossTracerTests</c>).
/// </summary>
public sealed class RealParserTraceTests
{
    // The exact grammar PanGloss's `trace_render.rs::golden_grammar` builds: one root, one suffix rule.
    private const string GoldenGrammar = """
        <?xml version="1.0" encoding="utf-8"?>
        <HermitCrabInput>
          <Language>
            <Name>Golden</Name>
            <PartsOfSpeech><PartOfSpeech id="posV"><Name>V</Name></PartOfSpeech></PartsOfSpeech>
            <HeadFeatures />
            <MorphologicalPhonologicalRuleFeatures>
              <MorphologicalPhonologicalRuleFeature id="mprA">Alpha</MorphologicalPhonologicalRuleFeature>
              <MorphologicalPhonologicalRuleFeatureGroup features="mprA"><Name>G</Name></MorphologicalPhonologicalRuleFeatureGroup>
            </MorphologicalPhonologicalRuleFeatures>
            <CharacterDefinitionTable id="t1">
              <Name>Main</Name>
              <SegmentDefinitions>
                <SegmentDefinition id="cS"><Representations><Representation>s</Representation></Representations></SegmentDefinition>
                <SegmentDefinition id="cA"><Representations><Representation>a</Representation></Representations></SegmentDefinition>
                <SegmentDefinition id="cG"><Representations><Representation>g</Representation></Representations></SegmentDefinition>
                <SegmentDefinition id="cD"><Representations><Representation>d</Representation></Representations></SegmentDefinition>
              </SegmentDefinitions>
              <BoundaryDefinitions>
                <BoundaryDefinition id="cPlus"><Representations><Representation>+</Representation></Representations></BoundaryDefinition>
              </BoundaryDefinitions>
            </CharacterDefinitionTable>
            <NaturalClasses>
              <SegmentNaturalClass id="ncAny"><Name>Any</Name><Segment segment="cS" /><Segment segment="cA" /><Segment segment="cG" /><Segment segment="cD" /></SegmentNaturalClass>
            </NaturalClasses>
            <Strata>
              <Stratum characterDefinitionTable="t1" morphologicalRuleOrder="unordered" morphologicalRules="mrEd">
                <Name>S</Name>
                <MorphologicalRuleDefinitions>
                  <MorphologicalRule id="mrEd" requiredPartsOfSpeech="posV"><Name>ed_suffix</Name><MorphemeId>PAST</MorphemeId>
                    <MorphologicalSubrules>
                      <MorphologicalSubrule id="subEd">
                        <MorphologicalInput><PhoneticSequence id="1"><OptionalSegmentSequence min="1" max="-1"><SimpleContext naturalClass="ncAny" /></OptionalSegmentSequence></PhoneticSequence></MorphologicalInput>
                        <MorphologicalOutput><CopyFromInput index="1" /><InsertSegments><PhoneticShape>+d</PhoneticShape></InsertSegments></MorphologicalOutput>
                      </MorphologicalSubrule>
                    </MorphologicalSubrules>
                  </MorphologicalRule>
                </MorphologicalRuleDefinitions>
                <LexicalEntries>
                  <LexicalEntry id="e32" partOfSpeech="posV"><MorphemeId>32</MorphemeId>
                    <Allomorphs><Allomorph id="a32"><PhoneticShape>sag</PhoneticShape></Allomorph></Allomorphs>
                  </LexicalEntry>
                </LexicalEntries>
              </Stratum>
            </Strata>
          </Language>
        </HermitCrabInput>
        """;

    [RealParserFact]
    public async Task ATracedWordReturnsTheVerbatimTreeAndAMatchingDerivedSummary()
    {
        var grammarPath = Path.Combine(Path.GetTempPath(), "motif-real-trace-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(grammarPath, GoldenGrammar);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        try
        {
            var outcome = await tracer.TraceAsync(grammarPath, "sagd", CancellationToken.None, TimeSpan.FromSeconds(30));

            var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
            Assert.Equal("32+PAST|sag+?d", completed.Summary.Signature);
            Assert.True(completed.Summary.Completed);
            Assert.Equal("ed_suffix", completed.Summary.DeepestRuleReached);
            Assert.Equal(
                ["NonPartialRuleProhibitedAfterFinalTemplate", "PartialParse"], completed.Summary.FailureReasons);
            Assert.NotNull(completed.Tree);
            Assert.Equal("WordAnalysis", completed.Tree!.Type);
            Assert.Equal("sagd", completed.Tree.InputShape);
        }
        finally
        {
            File.Delete(grammarPath);
        }
    }

    [RealParserFact]
    public async Task AGrammarTheParserCannotLoadIsDeclined()
    {
        var grammarPath = Path.Combine(Path.GetTempPath(), "motif-real-trace-bad-" + Guid.NewGuid().ToString("N") + ".xml");
        File.WriteAllText(grammarPath, "not xml at all");
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        try
        {
            var outcome = await tracer.TraceAsync(grammarPath, "sagd", CancellationToken.None, TimeSpan.FromSeconds(30));

            var declined = Assert.IsType<PanGlossTraceOutcome.Declined>(outcome);
            Assert.Contains("load", declined.Detail, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(grammarPath);
        }
    }
}
