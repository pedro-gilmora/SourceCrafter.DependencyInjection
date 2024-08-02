So, I'm thinking of a possible scenarios where the SGen initialization context can subscribe to some SGen *engine host* (1) delegate provided by some some delegate-publisher method belonging to SourceProducerContext, allowing to source generator authors (meaning sort of a core source generator author) emitting a check over an intermediary user-defined type metadata state, along with a predefined timeout and cancellation token. Thus third-party authors or same author writing can generate code based on what that delegate delivers to its extension source generator, making it able to generate integrator code to existing generated.
I think that approach might lead to create an execution, sort of tree-queue :grimacing:.

I know in advantage it would be a performance concern due to possible deadlocks and relying on extension SGen writers implementations.
Some addressings  of those possible issued would be making the delegate execution read-only as it it right now.

Theis would be my case:

Introducing of `CompilationInteropProvider` type and property to `IncrementalGeneratorInitializationContext`

```cs
public sealed class AnalyzerExtensibilityProvider
{
    static IncrementalValueProvider<T> SubscribeTo<T>(Func<T, CancellationToken ,bool> exchangedData)
}
```

```cs
public static class AnalyzerExtensibilityProviderExtensions
{
    // Generic is with the purpose of keying posible handlers by type hash code (T.GetHashCode)
    // Constrains for T it'll be up to the Roslyn team
    // A ver lazy name, also it'll up to the Roslyn team
    void Publish<T>(T dataToExchange, CancellationToken token);;
}
```

Core SGen author
```cs

var compilationProvider = incrementalGeneratorInitializationContext.CompilationProvider;
//Usage of [AnalyzerExtensibilityProvider] as a property
var extensibilityProvider = incrementalGeneratorInitializationContext.AnalyzerExtensibilityProvider;

collectedServiceContainerSymbols = context.SyntaxProvider
        .ForAttributeWithMetadataName(ServiceContainerAttributeFQName,
            (node, a) => true,
            (t, c) => (t.SemanticModel, Class: (INamedTypeSymbol)t.TargetSymbol)) 
        .Collect();

incrementalGeneratorInitializationContext.RegisterSourceOutput(

    compilationProvider
        .Combine( interopProvider )
        .Combine( collectedServiceContainerSymbols ), 

    (sourceProducer, ((compilation, interop), collectedServiceContainer), cancellationToken) => {
                                                                          //^ Introduced token to propagate among extensions

        foreach(var (semanticModel, serviceContainerSymbol)  in collectedServiceContainer)
        {
            ServiceContainerGenerator
                .DiscoverServicesFor(
                    compilation, 
                    semanticModel,
                    serviceContainerSymbol,
                    unresolvedDependencyHandler: (ServiceMetadata serviceDesc) =>
                                                /*^ Service metadata is hosted in a core-intermediary
                                                    maybe called {SGenNamespace}.{GenType}Metadata 
                                                    assembly reachable by other interested authors */
                    {
                        // Will publish to third-party consumers of this SGen
                        interop.Publish(serviceDesc, cancellationToken);
                    })
                .TryBuild(out string fileName, out string code);
        }

    });

```

I dont't know if it's something owning an already design sepc, or if it's a good enough API implementation, but it would be good to start, thinking as an average source generator author





(1) guessing there's something like that