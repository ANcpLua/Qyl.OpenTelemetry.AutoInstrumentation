```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M4, 1 CPU, 10 logical and 10 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-OIKEGS : .NET 10.0.8 (10.0.8, 10.0.826.23019), Arm64 RyuJIT armv8.0-a
  Job-GYYQXO : .NET 10.0.8, Arm64 NativeAOT armv8.0-a

IterationCount=5  LaunchCount=1  WarmupCount=3  

```
| Method              | Runtime        | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------- |--------------- |---------:|---------:|--------:|------:|--------:|-------:|----------:|------------:|
| DirectGetAsync      | .NET 10.0      | 155.1 ns |  2.18 ns | 0.57 ns |  1.00 |    0.00 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 164.6 ns | 10.50 ns | 1.62 ns |  1.06 |    0.01 | 0.0069 |     704 B |        1.00 |
|                     |                |          |          |         |       |         |        |           |             |
| DirectGetAsync      | NativeAOT 10.0 | 194.6 ns | 20.30 ns | 3.14 ns |  1.00 |    0.02 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 195.1 ns |  5.53 ns | 0.86 ns |  1.00 |    0.02 | 0.0069 |     704 B |        1.00 |
