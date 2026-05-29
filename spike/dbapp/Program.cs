// qyl breadth fixture — DATABASE domain (§07). Emits a semconv-shaped db.* CLIENT span.
using System.Diagnostics;

using var source = new ActivitySource("Qyl.Breadth.Db");
using (var act = source.StartActivity("SELECT mydb.users", ActivityKind.Client))
{
    act?.SetTag("db.system.name", "postgresql");
    act?.SetTag("db.namespace", "mydb");
    act?.SetTag("db.operation.name", "SELECT");
    act?.SetTag("db.collection.name", "users");
    act?.SetTag("server.address", "db.example.com");
}
Console.WriteLine("DB_DONE");
