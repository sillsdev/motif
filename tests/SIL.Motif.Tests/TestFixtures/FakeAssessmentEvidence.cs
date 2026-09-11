using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.TestFixtures;

internal static class FakeAssessmentEvidence
{
    public static BatchInvocationEvidence Capture(string root, AssessmentScope scope, string candidate)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
        var source = Path.Combine(directory, "source.fwdata");
        File.Copy(Directory.GetFiles(candidate, "*.fwdata", SearchOption.AllDirectories).Single(), source);
        var words = Path.Combine(directory, "words.txt");
        File.WriteAllLines(words, scope.Words);
        var tsv = Path.Combine(directory, "out.tsv");
        File.WriteAllText(tsv, string.Empty);
        var stderr = Path.Combine(directory, "stderr.txt");
        File.WriteAllText(stderr, string.Empty);
        return new BatchInvocationEvidence(id, source, BatchInvocationEvidence.DigestFile(source),
            BatchInvocationEvidence.DigestFile(FakeParser.ExecutablePath),
            words, BatchInvocationEvidence.DigestFile(words), tsv, BatchInvocationEvidence.DigestFile(tsv),
            stderr, BatchInvocationEvidence.DigestFile(stderr), (int)scope.PerWordLimit.TotalMilliseconds,
            scope.PerWordStepLimit, 1, true);
    }
}
