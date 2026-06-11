<!-- Filed 2026-06-11 as https://github.com/Particular/NServiceBus/issues/7817 -->
<!-- Template: Improvement request (.github/ISSUE_TEMPLATE/improvement_request.yml) -->
<!-- Issue title: -->
# Defer ConcreteProxyCreator's AssemblyBuilder construction so endpoints without interface messages can start under NativeAOT

## Describe the suggested improvement

#### Is your improvement related to a problem? Please describe.

`EndpointCreator.Configure()` unconditionally constructs `MessageMapper`, whose constructor immediately constructs `ConcreteProxyCreator`, whose constructor immediately calls `AssemblyBuilder.DefineDynamicAssembly`. Reflection.Emit is not available under NativeAOT, so this throws `PlatformNotSupportedException` during endpoint creation — before any message is mapped. Every endpoint therefore fails at startup under `PublishAot=true`, including endpoints that use only class messages and would never need an interface-message proxy. Because `IMessageMapper` is set internally via `settings.Set<IMessageMapper>(...)`, there is no consumer-side way to substitute it.

Minimal repro (net10.0 console app, NServiceBus 10.2.5, LearningTransport, assembly scanning disabled, explicit `AddHandler<T>()` registration, `SystemJsonSerializer` with a source-generated `JsonSerializerContext`, class messages only):

```bash
dotnet publish -c Release /p:PublishAot=true
./NsbAotRepro
```

```text
Unhandled exception. System.PlatformNotSupportedException: Dynamic code generation is not supported on this platform.
   at System.Reflection.Emit.ReflectionEmitThrower.ThrowPlatformNotSupportedException() + 0x30
   at NServiceBus.ConcreteProxyCreator..ctor() + 0x24
   at NServiceBus.MessageInterfaces.MessageMapper.Reflection.MessageMapper..ctor() + 0x15c
   at NServiceBus.EndpointCreator.Configure() + 0x228
   at NServiceBus.EndpointCreator.Create(EndpointConfiguration, IServiceCollection) + 0x214
   at NServiceBus.EndpointExternallyManaged.Create(EndpointConfiguration, IServiceCollection) + 0x18
   at NServiceBus.ServiceCollectionExtensions.AddNServiceBusEndpoint(IServiceCollection, EndpointConfiguration, Object) + 0x1d4
```

The same app runs and round-trips messages under JIT, so the 10.2 scanning-free registration path works; endpoint creation is the only remaining AOT blocker in this scenario.

#### Describe the suggested solution

Defer the `AssemblyBuilder` construction in `ConcreteProxyCreator` to the first interface-message proxy request. Endpoints that never map an interface message would then start under NativeAOT; endpoints that do would fail at the first proxy request with a clear platform error instead of at startup.

#### Describe alternatives you've considered

Annotating the interface-message surface with `[RequiresDynamicCode]` and documenting the boundary, so AOT consumers get a build-time warning instead of a startup crash. Either direction would resolve this — happy to follow whichever you prefer, and to submit a PR for it if that is welcome.

## Additional Context

Relevant code at 10.2.5:

- `src/NServiceBus.Core/EndpointCreator.cs`: `var messageMapper = new MessageMapper(); settings.Set<IMessageMapper>(messageMapper);`
- `src/NServiceBus.Core/MessageInterfaces/MessageMapper/Reflection/MessageMapper.cs`: `public MessageMapper() => concreteProxyCreator = new ConcreteProxyCreator();`
- `src/NServiceBus.Core/MessageInterfaces/MessageMapper/Reflection/ConcreteProxyCreator.cs`: constructor calls `AssemblyBuilder.DefineDynamicAssembly(...)`.

The 10.2.0 announcement describes scanning-free registration as ["a foundation for aligning with ahead-of-time compilation and trimming strategies in the future, even though full AOT and trimming compliance is not yet available"](https://discuss.particular.net/t/4626) — this suggestion targets the next blocker on that path. It is related to but not a duplicate of #7748, which covers source-generated entry-point discovery for handler-less assemblies rather than the Reflection.Emit dependency in endpoint creation.
