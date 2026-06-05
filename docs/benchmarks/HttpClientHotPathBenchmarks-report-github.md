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
| DirectGetAsync      | .NET 10.0      | 159.7 ns | 14.88 ns | 3.86 ns |  1.00 |    0.03 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | .NET 10.0      | 314.6 ns | 48.09 ns | 7.44 ns |  1.97 |    0.06 | 0.0114 |    1176 B |        1.67 |
|                     |                |          |          |         |       |         |        |           |             |
| DirectGetAsync      | NativeAOT 10.0 | 193.6 ns |  7.39 ns | 1.92 ns |  1.00 |    0.01 | 0.0069 |     704 B |        1.00 |
| InterceptedGetAsync | NativeAOT 10.0 | 339.8 ns |  8.82 ns | 2.29 ns |  1.76 |    0.02 | 0.0114 |    1176 B |        1.67 |
