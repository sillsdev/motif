namespace SIL.Motif.RecordStore;

/// <summary>The caller's store revision is no longer current.</summary>
public sealed class StaleRevisionException(string message) : InvalidOperationException(message);

/// <summary>The caller's expected prior field value differs from the stored value.</summary>
public sealed class StalePriorValueException(string message) : InvalidOperationException(message);

/// <summary>The acknowledged deletion plan is not the plan the store computes now.</summary>
public sealed class DeletionPlanMismatchException(string message) : InvalidOperationException(message);

/// <summary>Deletion usage closure is incomplete, so no deletion can be authorized.</summary>
public sealed class IncompleteDeletionClosureException(string message) : InvalidOperationException(message);

/// <summary>History holds a commit this handle did not write, so its replay digest cannot be advanced safely.</summary>
public sealed class ForeignCommitException(string message) : InvalidOperationException(message);

/// <summary>A list field cannot be written as a whole value; edit it through list operations.</summary>
public sealed class ListFieldWriteException(string message) : InvalidOperationException(message);

/// <summary>A list operation was malformed, beyond a limit, or addressed to no list; nothing was stored.</summary>
public sealed class ListOperationRefusedException(string message) : InvalidOperationException(message);
