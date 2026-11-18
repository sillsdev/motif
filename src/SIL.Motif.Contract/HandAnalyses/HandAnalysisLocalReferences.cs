using System;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.HandAnalyses;

internal static class HandAnalysisLocalReferences
{
    internal static void Validate(HandAnalysisInput input, List<HandAnalysisValidationError> errors)
    {
        var pieces = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Pieces.Count; i++)
            if (!pieces.Add(input.Pieces[i].PieceKey)) errors.Add(new($"/pieces/{i}/pieceKey", "Piece keys must be unique."));
        var keys = new Dictionary<string, (int Index, string Kind)>(StringComparer.Ordinal);
        var edges = new Dictionary<int, HashSet<int>>();
        for (var i = 0; i < input.Creations.Count; i++)
        {
            var creation = input.Creations[i];
            edges[i] = [];
            switch (creation)
            {
                case HandAnalysisEntryCreation entry:
                    Add(entry.Key, "entry", i, "key"); Add(entry.FormKey, "form", i, "formKey");
                    Add(entry.SenseKey, "sense", i, "senseKey"); Add(entry.MsaKey, "msa", i, "msaKey"); break;
                case HandAnalysisSenseCreation sense: Add(sense.Key, "sense", i, "key"); break;
                case HandAnalysisAllomorphCreation form:
                    Add(form.Key, "form", i, "key");
                    if (form.MsaKey is not null) Add(form.MsaKey, "msa", i, "msaKey"); break;
                case HandAnalysisVariantCreation variant: Add(variant.Key, "variant", i, "key"); break;
                case HandAnalysisVariantEntryCreation variant:
                    Add(variant.Key, "entry", i, "key"); Add(variant.FormKey, "form", i, "formKey");
                    Add(variant.RefKey, "variant", i, "refKey"); break;
            }
        }
        for (var i = 0; i < input.Creations.Count; i++)
        {
            var at = $"/creations/{i}";
            switch (input.Creations[i])
            {
                case HandAnalysisSenseCreation sense:
                    Reference(sense.Entry, "entry", at + "/entry", i);
                    Reference(sense.Msa.Reference, "msa", at + "/msa", i); break;
                case HandAnalysisAllomorphCreation form: Reference(form.Entry, "entry", at + "/entry", i); break;
                case HandAnalysisVariantCreation variant: Reference(variant.Entry, "entry", at + "/entry", i); break;
            }
        }
        for (var i = 0; i < input.Pieces.Count; i++)
        {
            var piece = input.Pieces[i]; var at = $"/pieces/{i}";
            Reference(piece.Link?.Entry, "entry", at + "/link/entry");
            Reference(piece.Link?.Allomorph, "form", at + "/link/allomorph");
            Reference(piece.Link?.Sense, "sense", at + "/link/sense");
            Reference(piece.Link?.Msa, "msa", at + "/link/msa");
            Reference(piece.VariantRef, "variant", at + "/variantRef");
        }
        var visited = new HashSet<int>(); var visiting = new HashSet<int>();
        foreach (var index in edges.Keys) Visit(index);

        void Add(string key, string kind, int index, string field)
        {
            if (HandAnalysisJson.TryId(key, out _) || !keys.TryAdd(key, (index, kind)))
                errors.Add(new($"/creations/{index}/{field}", "Creation keys must be unique local names, not stored ids."));
        }
        void Reference(string? value, string kind, string at, int? owner = null)
        {
            if (value is null || HandAnalysisJson.TryId(value, out _)) return;
            if (!keys.TryGetValue(value, out var target) || target.Kind != kind)
                errors.Add(new(at, "The local key is undeclared or names the wrong class."));
            else if (owner is { } index) edges[index].Add(target.Index);
        }
        void Visit(int index)
        {
            if (visited.Contains(index)) return;
            if (!visiting.Add(index)) { errors.Add(new($"/creations/{index}", "Creation dependencies must be acyclic.")); return; }
            foreach (var dependency in edges[index]) Visit(dependency);
            visiting.Remove(index); visited.Add(index);
        }
    }
}
