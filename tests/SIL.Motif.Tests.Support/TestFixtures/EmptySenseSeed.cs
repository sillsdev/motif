using SIL.LCModel;
using SIL.LCModel.Infrastructure;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>An entry shaped like a D-CEB-001 variant: one wholly empty sense whose stem analysis has no part of speech.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="SenseId">Its empty sense.</param>
/// <param name="MsaId">The stem analysis only that sense uses.</param>
public sealed record EmptySenseSeed(Guid EntryId, Guid SenseId, Guid MsaId)
{
    /// <summary>Adds one such entry, inside its own unit of work.</summary>
    public static EmptySenseSeed Add(LcmCache cache, string form, string? gloss = null, bool secondSenseSharesMsa = false)
    {
        var services = cache.ServiceLocator;
        ILexEntry entry = null!;
        ILexSense sense = null!;
        IMoStemMsa msa = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            entry = services.GetInstance<ILexEntryFactory>().Create();
            var lexemeForm = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = lexemeForm;
            lexemeForm.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>()
                .GetObject(MoMorphTypeTags.kguidMorphStem);
            lexemeForm.Form.set_String(cache.DefaultVernWs, form);
            msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            sense = services.GetInstance<ILexSenseFactory>().Create();
            entry.SensesOS.Add(sense);
            sense.MorphoSyntaxAnalysisRA = msa;
            if (gloss is not null)
                sense.Gloss.set_String(cache.DefaultAnalWs, gloss);
            if (secondSenseSharesMsa)
            {
                var other = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(other);
                other.MorphoSyntaxAnalysisRA = msa;
                other.Gloss.set_String(cache.DefaultAnalWs, "kept");
            }
        });
        return new EmptySenseSeed(entry.Guid, sense.Guid, msa.Guid);
    }
}
