namespace SIL.TextEditing.Project
{
    /// <summary>What a host can write to a project, asked of <see cref="ProjectRecordImporter"/> so that a change the host would refuse is refused when the import is
    /// planned and not when it is lowered. Names are liblcm's metadata names. The importer asks nothing of an instance it is not given, and treats a null one as
    /// "everything may be written". It is an abstract class so that a question can be added later as a virtual member with a documented default without breaking
    /// an implementer; the three questions below have no default because a wrong one would silently allow writes.</summary>
    public abstract class WritableFields
    {
        /// <summary>Whether the host can set, clear, add to, remove from or reorder the field. <paramref name="className"/> is the metadata class that declares the field
        /// (for a custom field, the class it was added to; for <c>Name</c> of a semantic domain, <c>CmPossibility</c>), so an implementation does not walk base classes.</summary>
        public abstract bool CanWrite(string className, string fieldName);

        /// <summary>Whether the host can create an object of <paramref name="className"/> (its concrete class) in the owned field <paramref name="ownerFieldName"/> declared by
        /// <paramref name="ownerClassName"/>.</summary>
        public abstract bool CanCreate(string ownerClassName, string ownerFieldName, string className);

        /// <summary>Whether the host can delete an object of <paramref name="className"/> (its concrete class) from the owned field <paramref name="ownerFieldName"/> declared by
        /// <paramref name="ownerClassName"/>.</summary>
        public abstract bool CanDelete(string ownerClassName, string ownerFieldName, string className);

        /// <summary>Whether the host can create an object of <paramref name="className"/> in that field as part of a new record, which may be wider than <see cref="CanCreate"/>
        /// because the host builds the whole new record at once. The default is <see cref="CanCreate"/>.</summary>
        public virtual bool CanCreateInNewRecord(string ownerClassName, string ownerFieldName, string className) => CanCreate(ownerClassName, ownerFieldName, className);
    }
}
