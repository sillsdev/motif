using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>Holds bounded promoted Assessment bindings to saved semantic project states.</summary>
public sealed class BeforeAssessmentBindingRepository(MotifDatabase database)
{
    /// <summary>Records a binding without altering the immutable Assessment.</summary>
    public void Bind(string assessmentId, BaselineSemanticIdentity identity)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO BeforeAssessmentBinding (AssessmentId, SemanticIdentityJson) " +
            "VALUES ($id, $identity); DELETE FROM BeforeAssessmentBinding WHERE Id NOT IN " +
            "(SELECT Id FROM BeforeAssessmentBinding ORDER BY Id DESC LIMIT 10) AND NOT EXISTS " +
            "(SELECT 1 FROM Jobs j, json_each(CASE WHEN json_valid(j.InputJson) THEN j.InputJson ELSE '{}' END, " +
            "'$.baselineAssessmentIds') x " +
            "JOIN Assessments held ON held.AssessmentId = x.value " +
            "JOIN Assessments bound ON bound.AssessmentId = BeforeAssessmentBinding.AssessmentId " +
            "WHERE j.Kind = 'trial' AND (held.AssessmentId = bound.AssessmentId OR " +
            "held.InvocationId = bound.InvocationId));";
        command.Parameters.AddWithValue("$id", assessmentId);
        command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(identity));
        command.ExecuteNonQuery();
    }

    /// <summary>Returns the bound Assessment only for the exact semantic identity.</summary>
    public string? Find(BaselineSemanticIdentity identity)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT AssessmentId FROM BeforeAssessmentBinding WHERE SemanticIdentityJson = $identity ORDER BY Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(identity));
        return command.ExecuteScalar() as string;
    }

    /// <summary>Returns the newest binding for one Assessment kind and semantic project state.</summary>
    public string? Find(BaselineSemanticIdentity identity, string kind)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b.AssessmentId FROM BeforeAssessmentBinding b " +
            "JOIN Assessments a ON a.AssessmentId = b.AssessmentId " +
            "WHERE b.SemanticIdentityJson = $identity AND a.Kind = $kind ORDER BY b.Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(identity));
        command.Parameters.AddWithValue("$kind", kind);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Returns the bound member of a particular invocation and Assessment kind.</summary>
    public string? Find(BaselineSemanticIdentity identity, string kind, string invocationId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT b.AssessmentId FROM BeforeAssessmentBinding b " +
            "JOIN Assessments a ON a.AssessmentId = b.AssessmentId WHERE b.SemanticIdentityJson = $identity " +
            "AND a.Kind = $kind AND a.InvocationId = $invocation ORDER BY b.Id DESC LIMIT 1;";
        command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(identity));
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$invocation", invocationId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Checks whether a specific Assessment is bound to this semantic project state.</summary>
    public bool Contains(string assessmentId, BaselineSemanticIdentity identity)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM BeforeAssessmentBinding WHERE AssessmentId = $id AND SemanticIdentityJson = $identity LIMIT 1;";
        command.Parameters.AddWithValue("$id", assessmentId);
        command.Parameters.AddWithValue("$identity", JsonSerializer.Serialize(identity));
        return command.ExecuteScalar() is not null;
    }
}
