using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

internal static class FixtureGuard
{
    internal const string Database = "core_source_sync_validation_20261109";
    private const string Owner = "core-source-sync-validation-v1";

    internal static bool AcceptsConnectionString(string connection)
    {
        try
        {
            var parsed = new SqlConnectionStringBuilder(connection);
            return string.Equals(parsed.InitialCatalog, Database, StringComparison.Ordinal)
                && string.IsNullOrEmpty(parsed.AttachDBFilename)
                && !parsed.UserInstance
                && string.IsNullOrEmpty(parsed.FailoverPartner)
                && Regex.IsMatch(parsed.DataSource,
                    @"\A(?:tcp:)?(?:127\.0\.0\.1|localhost|\[::1\])(?:,[0-9]{1,5})?\z",
                    RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static async Task VerifyOpenConnectionAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN DB_NAME() COLLATE Latin1_General_100_BIN2 = @database COLLATE Latin1_General_100_BIN2
              AND EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'core_source_sync_validation_owner'
                  AND CONVERT(nvarchar(100),value) COLLATE Latin1_General_100_BIN2=@owner COLLATE Latin1_General_100_BIN2)
              THEN 1 ELSE 0 END
            """;
        command.Parameters.AddWithValue("@database", Database);
        command.Parameters.AddWithValue("@owner", Owner);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            throw new InvalidOperationException("Owned isolated fixture guard failed; no test data was written.");
    }
}
