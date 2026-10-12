using System;
using System.Threading.Tasks;

Console.WriteLine("stdout:begin");
Console.Error.WriteLine("stderr:begin");
Console.WriteLine("result:" + Compute(40, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));

try
{
    ThrowExpected();
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("caught:" + exception.GetType().Name + ":" + exception.Message);
}

Console.WriteLine("async:" + await EchoAsync("done"));
Console.WriteLine("stdout:end");
Console.Error.WriteLine("stderr:end");
return 7;

static int Compute(int left, int right) => left + right;

static void ThrowExpected() => throw new InvalidOperationException("expected");

static async Task<string> EchoAsync(string value)
{
    await Task.Yield();
    return value;
}
