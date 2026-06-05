```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method                      | Runtime        | Mean       | Error     | StdDev    | Ratio  | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |--------------- |-----------:|----------:|----------:|-------:|--------:|----------:|------------:|
| DirectSqlClientCommand      | .NET 10.0      |  0.0274 ns | 0.0161 ns | 0.0042 ns |   1.02 |    0.20 |         - |          NA |
| InterceptedSqlClientCommand | .NET 10.0      |  5.5947 ns | 0.2304 ns | 0.0598 ns | 207.86 |   28.14 |         - |          NA |
|                             |                |            |           |           |        |         |           |             |
| DirectSqlClientCommand      | NativeAOT 10.0 |  0.0000 ns | 0.0000 ns | 0.0000 ns |      ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 10.3549 ns | 4.3590 ns | 1.1320 ns |      ? |       ? |         - |           ? |
