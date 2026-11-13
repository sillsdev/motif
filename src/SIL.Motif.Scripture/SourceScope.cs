namespace SIL.Motif.Scripture;

/// <summary>Where a chapter of a Source sits relative to the Context and the person's Current chapter.</summary>
public enum ChapterScope
{
    /// <summary>The chapter is in the Source's Context.</summary>
    InContext,

    /// <summary>The chapter is the person's Current chapter and is outside the Context.</summary>
    Current,

    /// <summary>The chapter is outside the Context and is not the Current chapter.</summary>
    Later,
}

/// <summary>Classifies the chapters of a Source against its Context; Later is always computed, never stored.</summary>
public static class SourceScope
{
    /// <summary>
    /// Places a chapter. A chapter in the Context is <see cref="ChapterScope.InContext"/> even when it is also
    /// Current. Throws <see cref="ArgumentException"/> when the Source holds no text for the chapter.
    /// </summary>
    public static ChapterScope Classify(
        ParatextSource source, ScriptureContext context, ChapterRef? current, ChapterRef chapter)
    {
        if (!source.HasChapter(chapter))
            throw new ArgumentException($"The Source '{source.Name}' has no text for {chapter}.", nameof(chapter));
        if (context.Contains(chapter.Book, chapter.Chapter)) return ChapterScope.InContext;
        return current == chapter ? ChapterScope.Current : ChapterScope.Later;
    }

    /// <summary>Every chapter of the Source that is not in the Context, in canonical order.</summary>
    public static IReadOnlyList<ChapterRef> Later(ParatextSource source, ScriptureContext context) =>
        source.Chapters.Where(chapter => !context.Contains(chapter.Book, chapter.Chapter)).ToList();

    /// <summary>The chapters of the Context that the Source holds, in canonical order.</summary>
    public static IReadOnlyList<ChapterRef> InContext(ParatextSource source, ScriptureContext context) =>
        source.Chapters.Where(chapter => context.Contains(chapter.Book, chapter.Chapter)).ToList();
}
