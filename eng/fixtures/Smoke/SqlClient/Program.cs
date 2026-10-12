using Microsoft.Data.SqlClient;

Console.WriteLine(typeof(SqlCommand).FullName);
return 0;

static int CompileTimeProbe(SqlCommand command) => command.ExecuteNonQuery();
