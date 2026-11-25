namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string OperationCascadingDelete = "operation.cascading-delete";
    public const string OperationInvalidDependency = "operation.invalid-dependency";
    /// <summary>An operation id is missing or not valid.</summary>
    public const string OperationInvalidId = "operation.invalid-id";
    /// <summary>An operation target is not a valid canonical id.</summary>
    public const string OperationInvalidTarget = "operation.invalid-target";
    /// <summary>An operation's writing system is empty.</summary>
    public const string OperationInvalidWritingSystem = "operation.invalid-writing-system";
}
