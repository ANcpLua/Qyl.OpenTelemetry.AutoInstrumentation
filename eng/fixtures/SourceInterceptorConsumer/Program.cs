using System.Diagnostics;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Qyl.Telemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(activityListener);

using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();

using (var create = connection.CreateCommand())
{
    create.CommandText = "CREATE TABLE Probe (Id INTEGER PRIMARY KEY, Name TEXT)";
    create.ExecuteNonQuery();
    create.CommandText = "INSERT INTO Probe (Id, Name) VALUES (1, 'ok')";
    create.ExecuteNonQuery();
}

var calls = 0;

using (DbCommand command = connection.CreateCommand())
{
    command.CommandText = "SELECT Name FROM Probe WHERE Id = 1";
    _ = command.ExecuteScalar();
    calls++;
}

using (DbCommand command = connection.CreateCommand())
{
    command.CommandText = "SELECT Name FROM MissingProbe WHERE Id = 1";
    try
    {
        _ = command.ExecuteScalar();
    }
    catch (SqliteException)
    {
        // The failing call is the second half of the control: the interceptor has to record the
        // error as well as the success, which is what makes it an instrumentation proof rather
        // than a "did anything get emitted" check.
    }

    calls++;
}

var dbActivities = captured
    .Where(static activity => activity.TagObjects.Any(static tag =>
        tag.Key == "qyl.instrumentation.domain" &&
        StringComparer.Ordinal.Equals(tag.Value as string, "db.client")))
    .ToArray();
var outcomes = dbActivities
    .Select(static activity => activity.Status is ActivityStatusCode.Error ? "error" : "ok")
    .OrderBy(static outcome => outcome, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine("client.calls=" + calls.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.count=" + dbActivities.Length.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.statuses=" + string.Join("|", outcomes));

return calls == 2 && dbActivities.Length == 4 ? 0 : 1;
