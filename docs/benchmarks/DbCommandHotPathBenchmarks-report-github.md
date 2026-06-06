```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method                      | Runtime        | Mean      | Error     | StdDev    | Median    | Ratio    | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |--------------- |----------:|----------:|----------:|----------:|---------:|--------:|----------:|------------:|
| DirectSqlClientCommand      | .NET 10.0      | 0.0139 ns | 0.0547 ns | 0.0142 ns | 0.0062 ns |     2.88 |    4.16 |         - |          NA |
| InterceptedSqlClientCommand | .NET 10.0      | 5.1856 ns | 0.0964 ns | 0.0149 ns | 5.1900 ns | 1,070.68 |  880.41 |         - |          NA |
|                             |                |           |           |           |           |          |         |           |             |
| DirectSqlClientCommand      | NativeAOT 10.0 | 0.0000 ns | 0.0000 ns | 0.0000 ns | 0.0000 ns |        ? |       ? |         - |           ? |
| InterceptedSqlClientCommand | NativeAOT 10.0 | 9.1525 ns | 1.4298 ns | 0.3713 ns | 9.1078 ns |        ? |       ? |         - |           ? |
