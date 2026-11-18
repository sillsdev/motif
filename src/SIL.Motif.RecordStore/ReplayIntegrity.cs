using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

internal static class ReplayIntegrity
{
    public static async Task<string> DigestAsync(RecordDatabase database)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await database.Database.OpenConnectionAsync();
        try
        {
            foreach (var (table, order) in new[] { ("Commits", "Id"), ("ChangeEntities", "CommitId,\"Index\"") })
            {
                await using var command = database.Database.GetDbConnection().CreateCommand();
                command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction();
                command.CommandText = $"SELECT * FROM {table} ORDER BY {order}";
                await using var reader = await command.ExecuteReaderAsync();
                using (var header = new BinaryOutput())
                {
                    header.Text(table); header.Unsigned((ulong)reader.FieldCount);
                    for (var index = 0; index < reader.FieldCount; index++) header.Text(reader.GetName(index));
                    hash.AppendData(header.Finish());
                }
                while (await reader.ReadAsync())
                {
                    using var row = new BinaryOutput();
                    row.Byte(1);
                    for (var index = 0; index < reader.FieldCount; index++)
                    {
                        switch (reader.GetValue(index))
                        {
                            case DBNull: row.Byte(0); break;
                            case long value: row.Byte(1); row.Integer(value); break;
                            case double value: row.Byte(2); row.Integer(BitConverter.DoubleToInt64Bits(value)); break;
                            case string value: row.Byte(3); row.Text(value); break;
                            case byte[] value: row.Byte(4); row.Bytes(value); break;
                            default: throw new InvalidDataException("Unsupported stored replay value.");
                        }
                    }
                    hash.AppendData(row.Finish());
                }
                hash.AppendData(new byte[] { 0 });
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        finally { await database.Database.CloseConnectionAsync(); }
    }
}
