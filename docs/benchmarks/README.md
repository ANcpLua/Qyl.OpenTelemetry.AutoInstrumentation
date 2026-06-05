# Hot-path Benchmark Reports

The committed files in this directory are BenchmarkDotNet GitHub markdown exports
for the runtime autoinstrumentation hot paths.

Regenerate them with:

```bash
tools/run-hotpath-benchmarks.sh
```

The benchmark project runs each hot path under both .NET 10 JIT and .NET 10 NativeAOT.
The fast repository gate verifies the benchmark project, smoke-executes the harness,
and checks that committed reports contain both runtimes and allocation columns.
