using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class EditTextNotesDraftTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ComposeStagesOneOperationPerSegmentFromJsonOrTsvWithoutChangingTheProject()
    {
        string path;
        using (var cache = pristine.NewScratch())
        {
            path = cache.ProjectId.Path;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var text = cache.ServiceLocator.GetInstance<ITextFactory>().Create();
                text.Name.set_String(cache.DefaultAnalWs, "Tracking");
                text.ContentsOA = cache.ServiceLocator.GetInstance<IStTextFactory>().Create();
                var para = cache.ServiceLocator.GetInstance<IStTxtParaFactory>().Create();
                text.ContentsOA.ParagraphsOS.Add(para);
                para.Contents = TsStringUtils.MakeString("alpha. beta.", cache.DefaultVernWs);
                ParagraphParser.ParseParagraph(para);
            });
            new FwDataProjectLoader().Save(cache);
        }
        Assert.True(ProposalCommands.New(new(path, "1.0", "notes", null)).Succeeded);

        var json = ProposalCommands.ComposeIntent(new(path, "1.0", "notes", "compose-edit-text-notes",
            JsonSerializer.Serialize(new { edits = new[] { new { text = "Tracking", word = "beta", note = "proper name" } } })));
        Assert.True(json.Succeeded, json.Refusal?.Message);
        Assert.Equal(EditTextNotesOperationKinds.Set, Assert.Single(json.Value!.Operations).Kind);

        var tsv = ProposalCommands.ComposeIntent(new(path, "1.0", "notes", "compose-edit-text-notes",
            EditTextNotesIntentParser.FromTsv("Tracking\t1\tloan: English\n").GetRawText()));
        Assert.True(tsv.Succeeded, tsv.Refusal?.Message);

        var unknown = ProposalCommands.ComposeIntent(new(path, "1.0", "notes", "compose-edit-text-notes",
            JsonSerializer.Serialize(new { edits = new[] { new { text = "Nope", segment = 1, note = "x" } } })));
        Assert.False(unknown.Succeeded);
        Assert.Contains("no text named", unknown.Refusal!.Message);

        using var reopened = new FwDataProjectLoader().LoadCache(path);
        Assert.All(reopened.ServiceLocator.GetInstance<ITextRepository>().AllInstances()
                .SelectMany(t => t.ContentsOA.ParagraphsOS.Cast<IStTxtPara>()).SelectMany(p => p.SegmentsOS),
            segment => Assert.Empty(segment.NotesOS));
    }
}
