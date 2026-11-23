using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using Xunit;
using Xunit.Abstractions;

namespace SIL.TextEditing.Project.Tests
{
    /// <summary>The real-data export and no-op import runs, on a synthetic project built in the test, so they run in every CI job; counts only are printed.</summary>
    public class ProjectRecordSyntheticCountsTests
    {
        readonly ITestOutputHelper _output;
        public ProjectRecordSyntheticCountsTests(ITestOutputHelper output) { _output = output; }

        static ProjectFixture Populated()
        {
            var fixture = new ProjectFixture();
            var cache = fixture.Cache;
            fixture.Change(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create();
                    var form = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
                    entry.LexemeFormOA = form;
                    form.Form.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("zzword" + i, cache.DefaultVernWs));
                    form.MorphTypeRA = (IMoMorphType)cache.LangProject.LexDbOA.MorphTypesOA.PossibilitiesOS.First();
                    var sense = cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
                    entry.SensesOS.Add(sense);
                    sense.Gloss.set_String(cache.DefaultAnalWs, TsStringUtils.MakeString("zzgloss" + i, cache.DefaultAnalWs));
                    var example = cache.ServiceLocator.GetInstance<ILexExampleSentenceFactory>().Create();
                    sense.ExamplesOS.Add(example);
                    example.Example.set_String(cache.DefaultVernWs, TsStringUtils.MakeString("zzexample" + i, cache.DefaultVernWs));
                }
            });
            return fixture;
        }

        [Fact]
        public void EveryRecordOfASyntheticProjectExportsAndReadsBackWithOnlyKnownLosses()
        {
            using var fixture = Populated();
            ProjectRecordExportRealDataTests.RunExportAndReadBack(fixture, _output);
        }

        [Fact]
        public void ANoOpFileForEveryRecordOfASyntheticProjectPlansNoChangeAndNoRefusal()
        {
            using var fixture = Populated();
            ProjectRecordImportRealDataTests.RunNoOpPlan(fixture, _output);
        }
    }
}
