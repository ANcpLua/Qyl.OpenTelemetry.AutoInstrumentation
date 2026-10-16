global using Microsoft.VisualStudio.TestTools.UnitTesting;
global using Qyl.Build;
global using Serilog;

// The applications bind ports and the AOT publisher owns a shared build cache.
[assembly: DoNotParallelize]
