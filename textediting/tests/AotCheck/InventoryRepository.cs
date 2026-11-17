using System;
using System.Collections.Generic;
using System.Linq;
using SIL.WritingSystems;

/// <summary>A repository that lists writing-system ids and nothing else, to drive the repository-backed checker under AOT.</summary>
sealed class InventoryRepository : IWritingSystemRepository
{
    public InventoryRepository(params string[] tags) => Definitions = tags.Select(tag => new WritingSystemDefinition { Id = tag }).ToList();
    public List<WritingSystemDefinition> Definitions { get; }
    public IEnumerable<WritingSystemDefinition> AllWritingSystems => Definitions;
    public event EventHandler<WritingSystemIdChangedEventArgs> WritingSystemIdChanged { add { } remove { } }
    public event EventHandler<WritingSystemDeletedEventArgs> WritingSystemDeleted { add { } remove { } }
    public event EventHandler<WritingSystemConflatedEventArgs> WritingSystemConflated { add { } remove { } }
    public int Count => throw new NotSupportedException();
    public IWritingSystemFactory WritingSystemFactory => throw new NotSupportedException();
    public void Set(WritingSystemDefinition ws) => throw new NotSupportedException();
    public bool CanSet(WritingSystemDefinition ws) => throw new NotSupportedException();
    public WritingSystemDefinition Get(string id) => throw new NotSupportedException();
    public bool TryGet(string id, out WritingSystemDefinition ws) => throw new NotSupportedException();
    public string GetNewIdWhenSet(WritingSystemDefinition ws) => throw new NotSupportedException();
    public bool Contains(string id) => throw new NotSupportedException();
    public void Conflate(string wsToConflate, string wsToConflateWith) => throw new NotSupportedException();
    public void Remove(string id) => throw new NotSupportedException();
    public bool WritingSystemIdHasChanged(string id) => throw new NotSupportedException();
    public string WritingSystemIdHasChangedTo(string id) => throw new NotSupportedException();
    public bool CanSave(WritingSystemDefinition ws) => throw new NotSupportedException();
    public void Save() => throw new NotSupportedException();
}
