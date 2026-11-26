using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Project
{
    /// <summary>What the other files of one batch create and delete, so a file's references and links can name an object that is not in the project yet. The batch is read-only
    /// and is built by the host before any file is planned; a new object is known by its GUID and the class its file gives it.</summary>
    public sealed class RecordBatch
    {
        /// <param name="newObjects">Every object the batch's files create (a new record and the objects it owns, or an object an update adds), by GUID, with its class.</param>
        /// <param name="deleted">Every object the batch's files remove, by GUID, with the label of the file that removes it.</param>
        /// <param name="self">The label of the file being planned: what it removes does not count against its own references.</param>
        public RecordBatch(IReadOnlyDictionary<Guid, string> newObjects, IReadOnlyDictionary<Guid, string> deleted, string? self = null)
        {
            NewObjects = newObjects ?? throw new ArgumentNullException(nameof(newObjects));
            Deleted = deleted ?? throw new ArgumentNullException(nameof(deleted));
            Self = self;
        }

        public IReadOnlyDictionary<Guid, string> NewObjects { get; }

        public IReadOnlyDictionary<Guid, string> Deleted { get; }

        public string? Self { get; }

        /// <summary>The same batch seen from the file labelled <paramref name="self"/>.</summary>
        public RecordBatch For(string self) => new RecordBatch(NewObjects, Deleted, self);

        /// <summary>The label of the other file that removes <paramref name="guid"/>, or null.</summary>
        public string? DeletedBy(Guid guid) => Deleted.TryGetValue(guid, out var by) && !string.Equals(by, Self, StringComparison.Ordinal) ? by : null;
    }
}
