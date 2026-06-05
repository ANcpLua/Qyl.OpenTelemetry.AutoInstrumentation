```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method                      | Runtime        | Mean      | Error     | StdDev    | Ratio  | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |--------------- |----------:|----------:|----------:|-------:|--------:|----------:|------------:|
| DirectSqlClientCommand      | .NET 10.0      | 0.0403 ns | 0.0126 ns | 0.0033 ns |   1.01 |    0.10 |         - |          NA |
| InterceptedSqlClientCommand | .NET 10.0      | 5.1818 ns | 0.0220 ns | 0.0057 ns | 129.29 |    9.23 |         - |          NA |
|                             |                |           |           |           |        |         |           |             |
| DirectSqlClientCommand      | NativeAOT 10.0 | 0.0000 ns | 0.0000 ns | 0.0000 ns |      ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 8.6665 ns | 0.1217 ns | 0.0316 ns |      ? |       ? |         - |           ? |
