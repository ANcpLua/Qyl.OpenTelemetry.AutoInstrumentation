```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method              | Runtime        | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------- |--------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| DirectGetAsync      | .NET 10.0      | 153.3 ns | 1.25 ns | 0.32 ns |  1.00 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 296.9 ns | 0.73 ns | 0.19 ns |  1.94 | 0.0114 |    1176 B |        1.67 |
|                     |                |          |         |         |       |        |           |             |
| DirectGetAsync      | NativeAOT 10.0 | 187.2 ns | 2.29 ns | 0.35 ns |  1.00 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 331.9 ns | 5.14 ns | 1.34 ns |  1.77 | 0.0114 |    1176 B |        1.67 |
