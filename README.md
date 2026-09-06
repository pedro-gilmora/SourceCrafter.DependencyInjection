# SourceCrafter.DependencyInjection

Truly compile-time dependency injection for .NET. Services are declared with attributes and
the Roslyn generator emits plain, readable C# resolvers — no reflection, no expression
trees, no runtime container.

```csharp
[ServiceContainer]
[Singleton<IClock, SystemClock>]
[Scoped<DbSession>]
[Transient<Handler>]
public partial class AppContainer { }

using var container = new AppContainer();
var handler = container.CreateScope().Handler;
```

**Full documentation lives in [`SourceCrafter.DependencyInjection/README.md`](SourceCrafter.DependencyInjection/README.md)** —
attribute reference, generated code walkthrough, cancellation semantics and the `SCDI`
diagnostic table.

## Packages

| Package | What it is |
|---------|------------|
| `SourceCrafter.DependencyInjection` | The generator. This is the one you usually want. |
| `SourceCrafter.DependencyInjection.Metadata` | Attributes only, no generator. For libraries that want to declare contracts without pulling in the generator. |
| `SourceCrafter.DependencyInjection.MsConfiguration` | The generator plus `Microsoft.Extensions.Configuration` support (`[JsonSetting<T>]`). Use instead of the core generator. |
| `SourceCrafter.DependencyInjection.MsConfiguration.Metadata` | Attributes only, configuration flavour. |

## Repository layout

- `SourceCrafter.DependencyInjection/` — the generator (netstandard2.0 is deliberately not targeted)
- `SourceCrafter.DependencyInjection.Metadata/` — attribute definitions
- `SourceCrafter.DependencyInjection.MsConfiguration*/` — configuration-aware variants
- `SourceCrafter.DependencyInjection.Tests/` — the test suite
- `Benchmarks/` — BenchmarkDotNet harness

## Building and testing

```bash
dotnet build SourceCrafter.DependencyInjection.slnx
```

The test project targets .NET 10 and runs through the xUnit v3 in-process runner. `dotnet test`
is currently unreliable with the .NET 10 SDK here, so run the produced executable directly:

```bash
dotnet build SourceCrafter.DependencyInjection.Tests/SourceCrafter.DependencyInjection.Tests.csproj
SourceCrafter.DependencyInjection.Tests/bin/Debug/net10.0/SourceCrafter.DependencyInjection.Tests.exe
```

Filter with `-method "*SomePattern*"`.

### Test layers

Changing the generator means keeping five different kinds of test honest:

- **`Tests.cs` / `Server.cs`** — behaviour. A realistic container is compiled by the real
  generator during the build, then exercised at runtime.
- **`GeneratedCodeTests.cs`** — emission. Runs the generator in memory via
  `GeneratorHarness` and asserts on the *text* it produces: lock scoping, the constructor,
  disposal ordering, file shape, and determinism across repeated passes.
- **`RootPropagationTests.cs`** — the `Root`/`CreateScope` virtual-dispatch contract.
- **`LockingStrategyTests.cs`** — the locking scheme itself, modelled by hand with no
  generator involved. Shows why one lock per lifetime deadlocks on cross-lifetime
  dependencies, why a single lock is correct but serializing, and why an instance lock
  cannot guard a static field.
- **`EqualityContractTests.cs`** — guards every generator model type against declaring
  `Equals` without a matching `==`, which silently breaks incremental caching.

If you touch the emitter, `GeneratedCodeTests` is where a regression will surface first.

### Inspecting generated output

```bash
dotnet build -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/generated
```

Emit to a directory *outside* the project folder, or the generated files get picked up by the
next compilation and every type ends up declared twice.

### Benchmarks

`Benchmarks/` compares this generator against every actively maintained compile-time DI
container — [Jab](https://github.com/pakrym/jab),
[Pure.DI](https://github.com/DevTeam/Pure.DI),
[StrongInject](https://github.com/YairHalberstadt/stronginject) and
[MrMeeseeks.DIE](https://github.com/Yeah69/MrMeeseeks.DIE) — following the scenarios that
[.NET Matrix](https://github.com/DevTeam/dotnet-matrix) has made the de facto standard for
this category. Results are in the package README.

```bash
dotnet run -c Release --project Benchmarks -- --filter "*"
```

Two rules the harness must keep:

- **Every branch does exactly the same work.** An earlier version of this harness only built
  the container in the SourceCrafter branch while the others created a scope and resolved a
  service, so the published numbers compared nothing.
- **`Hand Coded` is the baseline**, not another container. Nested `new(...)` is the floor a
  compile-time container is trying to reach.

And one rule for reading the output:

- **Do not publish a timing ratio from a single run.** These scenarios resolve in tens of
  nanoseconds, where per-process JIT and heap-layout luck is a large fraction of the
  measurement. A single-launch run once showed all three containers jumping from ~38 ns to
  ~80 ns while the baseline held still — three independent libraries do not regress in
  lockstep. `HarnessConfig` therefore pins three process launches plus memory randomization,
  and even then the complex-graph scenario has been seen to vary 0.95x–2.10x across runs of
  the same binary with byte-identical allocations. **Allocation counts are reproducible;
  sub-100 ns timings are only approximately so.**

Where a library genuinely lacks a feature (StrongInject and MrMeeseeks.DIE have no request
scope), the row is left empty rather than approximated: a made-up equivalent would measure
something no user of that library could write.

Each container declaration lives in its own `Containers.*.cs` file, because the libraries ship
attributes with colliding names (`[Transient<,>]`, `[Singleton<,>]`, `[Scoped<,>]`,
`[Register]`).

## Contributing

Issues and pull requests are welcome. A few conventions that are not obvious from the code:

- The generator project sets `EnableDefaultCompileItems=false`. **Any new `.cs` file must be
  added to the `.csproj` by hand**, or it will silently not compile.
- Parsing and emission are separated on purpose. Anything reachable from a cached delegate
  must be free of `ISymbol`, `SyntaxNode`, `SemanticModel` and `Compilation`, otherwise the
  incremental pipeline keeps whole Roslyn compilations alive. `ResolverRenderer` is the
  frozen, symbol-free projection that the emitter renders from.
- New diagnostics go in `ServiceContainerDiagnostics.cs` with a fresh, unique `SCDI` id, and
  a `DiagnosticDescriptor.Title` that is static text (dynamic titles break IDE grouping).
- Generated code is normalized to tabs, CRLF and no trailing whitespace before it is emitted.

## License

See [LICENSE.txt](LICENSE.txt).