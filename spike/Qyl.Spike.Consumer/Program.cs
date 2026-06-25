using Qyl.Spike.Generated;

// This file compiles ONLY if both generated types exist:
//   PreCompilationProbe        -> emitted by RegisterPreCompilationSourceOutput (pre-compilation phase)
//   StandardPhaseConfirmation  -> emitted by RegisterSourceOutput AFTER binding the marker (standard phase)
// A green build is therefore unfalsifiable proof the two-phase contract works end to end.

Console.WriteLine($"PreCompilationProbe.AdditionalFileCount      = {PreCompilationProbe.AdditionalFileCount}");
Console.WriteLine($"StandardPhaseConfirmation.ObservedAdditionalFileCount = {StandardPhaseConfirmation.ObservedAdditionalFileCount}");
Console.WriteLine("GATE-0 SPIKE: pre-compilation contract proven end to end.");
