using System.Diagnostics.CodeAnalysis;
using System.Data;
using System.Data.Common;

// The interceptable receiver the snapshot is built on. ADO.NET is the lane that stays an
// interceptor: System.Data.Common declares no ActivitySource, so nothing subscribes it and the
// generated call-site interceptor is the only producer.
internal class SnapshotCommand : DbCommand
{
    [AllowNull]
    public override string CommandText { get; set; } = "SELECT 1";

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => 0;

    public override object? ExecuteScalar() => null;

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}
