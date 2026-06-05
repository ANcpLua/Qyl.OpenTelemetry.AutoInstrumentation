```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method                   | Runtime        | Mean       | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------- |--------------- |-----------:|----------:|----------:|------:|--------:|----------:|------------:|
| DirectExecuteSqlRaw      | .NET 10.0      |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | .NET 10.0      |  8.8453 ns | 0.0293 ns | 0.0076 ns |     ? |       ? |         - |           ? |
|                          |                |            |           |           |       |         |           |             |
| DirectExecuteSqlRaw      | NativeAOT 10.0 |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |         - |           ? |
| InterceptedExecuteSqlRaw | NativeAOT 10.0 | 11.4763 ns | 0.1309 ns | 0.0203 ns |     ? |       ? |         - |           ? |
