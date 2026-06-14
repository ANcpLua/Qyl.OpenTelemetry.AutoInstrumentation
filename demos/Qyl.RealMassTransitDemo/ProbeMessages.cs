using System.Text.Json.Serialization;

namespace Qyl.RealMassTransitDemo;

/// <summary>Probe event published through the real bus.</summary>
public sealed record ProbeEvent(string Name);

/// <summary>Probe command sent to an explicit queue endpoint.</summary>
public sealed record ProbeCommand(string Name);

/// <summary>Source-generated JSON metadata so message serialization works under NativeAOT.</summary>
[JsonSerializable(typeof(ProbeEvent))]
[JsonSerializable(typeof(ProbeCommand))]
public sealed partial class ProbeMessageJsonContext : JsonSerializerContext;
