# SourceCrafter.DependencyInjection - Internals

Design rationale, measurements and generated-code anatomy.

The [README](README.md) covers the design guidelines and the public surface. This document
covers *why* the generated code looks the way it does, and what was measured to decide it.

---
## Async composition and interceptor caches

### How async dependencies are composed

Independent async dependencies are **started first, then awaited**, so they progress
concurrently:

```csharp
var __v0 = GetDatabaseAsync();
var __v1 = GetCountAsyncCached();

return _authServiceTask = __v0.IsCompletedSuccessfully && __v1.IsCompletedSuccessfully
    ? Task.FromResult(new AuthService(__v0.Result, __v1.Result))   // fast path, no state machine
    : ResolveCoreAsync();

async Task<IAuthService> ResolveCoreAsync()
    => new AuthService(
        await __v0.ConfigureAwait(false),
        await __v1.ConfigureAwait(false));
```

Two decisions here are worth spelling out, because the obvious alternatives are worse:

**No `Task.WhenAll`.** It looks like the idiomatic way to await several tasks, but it buys
nothing once the tasks are already started — awaiting them one by one does not serialize
them. It only adds work: the `Task[]` for the `params` argument plus the promise that
tracks them. Measured on the hardened harness (3 launches, memory randomization):

| shape | time | allocated |
|---|---:|---:|
| `new X(await v0, v1.Result)` | 11.6 ns | 96 B |
| `new X(await v0, await v1)` | 23.2 ns | 96 B |
| `await WhenAll(v0, v1)` + `.Result` | 44.1 ns | 256 B |

**Dependencies already resolved by an earlier parameter only read `.Result`.** When a
parameter's subtree has already completed another parameter's task, awaiting it again would
cost a second state machine transition for a value that is already there:

```csharp
return new EmployeeController(
    await __v0.ConfigureAwait(false),
    __v1.Result /* resolved previously by param 0 */,
    __v2.Result /* resolved previously by param 0 */);
```

Not using `WhenAll` did leave one gap: if two dependencies fault at once, the first `await`
throws and the sibling task's exception is never observed, which raises
`TaskScheduler.UnobservedTaskException`. Multi-service interceptors close it on the way out,
which costs nothing — the method is already `async`, so the compiler has *already* wrapped its
`MoveNext` in a `try`/`catch` to deposit the exception into the returned task; ours nests
inside an existing one. The measurement agrees: `sequential + observe` is 1.26 ± 0.64 against
1.13 ± 0.55 for plain sequential, at identical allocation.

```csharp
var __t0 = provider.FirstAsyncCached;
var __t1 = provider.SecondAsyncCached;

try
{
	return provider.__interceptorCache12 = [
		await __t0,
		await __t1];
}
catch
{
	_ = __t0.Exception;
	_ = __t1.Exception;

	throw;
}
```

The original exception still reaches the caller unwrapped — observing a sibling does not
change what is thrown. This is emitted only when **every** async element is a `Task<T>`:
`ValueTask<T>` exposes no `Exception`, and `AsTask()` on one that has already been consumed
throws `InvalidOperationException`, so there is no way to observe it without consuming it.
Single-service resolvers are unaffected — they only ever have one task to await.

Interceptors that return several services apply the same idea across array elements. All
tasks are started first; then only the elements that no other element already resolves are
awaited, and the rest read `.Result`:

```csharp
var __t0 = provider.AlphaAsyncCached;
var __t1 = provider.GetBetaAsync();
var __t2 = provider.GetGammaAsync();
var __r0 = await __t2;

return [
    __t0.Result /* resolved previously by __t2 */,
    __t1.Result /* resolved previously by __t2 */,
    __r0];
```

Only *cached* elements (singleton or scoped) qualify: a transient hands out a fresh task on
every call, so the one another element awaited internally is not the one this array holds.
Reading `.Result` never degrades an exception into an `AggregateException` either, because
the covering element depends on the covered one — if the covered task faults, so does the
covering one, and its `await` throws first.


### Caching of the array returned by the plural overloads

The plural overloads build a `T[]`. That array is cached, but only when caching cannot
change what the container promises:

| Elements | Cache |
| --- | --- |
| all `Singleton` | `static` field on the generated interceptor class |
| any `Scoped`, no `Transient` | instance field on the container, so each `CreateScope()` gets its own |
| any `Transient` | **not cached** |

A single transient is enough to disable it. A transient promises a fresh instance per call,
so keeping the array would silently turn that element into a singleton — this is a
semantic rule, not an optimization trade-off. The lifetime of the array is the *minimum*
lifetime of its elements.

Two notes on what this implies:

- The array is shared, so **do not mutate what these methods return**. Mutating it corrupts
  every later caller. The signature stays `T[]` (rather than `ImmutableArray<T>`) because
  the generated code must compile on `netstandard2.0` consumers, where
  `System.Collections.Immutable` is not in-box.
- The async overloads cache the resolved **array**, never the `Task`. A cached task would
  keep a failure alive for the rest of the process; a resolved array cannot.

No lock guards the cache: every element is itself cached, so two racing callers build
arrays holding the very same instances. The only cost of losing that race is the allocation
it was trying to save.

---

## Architecture & Performance

### Generated Code Structure

The generator creates:

1. **Cached Properties/Fields** (with lock guards):
   - Singleton services: `static` fields guarded by a `static readonly` lock
   - Scoped services: instance fields guarded by an instance lock created on first use

   The lock always matches the scope of the field it guards, so lazy initialization is
   genuinely thread-safe even across several container instances. Instance locks are
   allocated lazily, so opening a scope costs nothing for services it never resolves.

2. **Resolver Methods**:
   - Direct instantiation for transients
   - Cached lookup + return for singletons/scoped
   - Async-aware parallel resolution for Task-based dependencies

3. **Disposal Routing**:
   - Automatic tracking of disposable dependencies
   - Proper disposal order in `DisposeAsync()` / `Dispose()`

### Why dependency-less transients are invisible by default

A transient whose factory takes no parameters is a *simple transient*: the parser short-circuits
in `TryRegisterService` and never registers a member builder, so the container exposes nothing
for it. The interceptor emits `new Leaf()` straight at the call site, which is strictly faster
than routing through a member.

The cost is a real hole rather than a cosmetic one. Interception is scoped to the compilation
that runs the generator: `[InterceptsLocation]` refers to syntax trees of *that* compilation, so
a consumer in another assembly is never rewritten. Verified with two projects — a library
declaring `[Singleton<Node>] [Transient<Leaf>]`, and a consumer referencing only the library:

| call from the consumer | result |
|---|---|
| `c.Node` | resolves |
| `c.Leaf` | **CS1061** — the member does not exist |
| `c.GetRequiredService<Leaf>()` | compiles, then throws **`NotImplementedException`** at run time |

Every other registration has a named member as its safety net; the simple transient was the
only one without, and its failure mode was the worst of the three.

`exportTransients: true` registers the member builder in that early exit. It does **not** touch
inlining: `TransientWithoutCachedDeps` still holds, so call sites inside the declaring
compilation keep building the instance in place, and the member exists purely for outside
consumers.

Two details worth knowing:

- The option is read in a pre-pass over the type's attributes, before any service is
  registered. It used to be read lazily from inside the same loop that registers services,
  which would have made the result depend on whether the author wrote `[ServiceProvider]`
  above or below the `[Transient<T>]` attributes.
- The exported member follows the existing shape rules, so a sync transient becomes a property
  (`public Leaf Leaf => new Leaf();`) and an async factory becomes a property returning the
  task (`public Task<Far> FarAsync => _GetFarAsync();`). A property still allocates on every
  read, which the debugger will do on every step; making transients method-shaped remains an
  open design question, not something this option decided.

### Member naming

The name of a resolver backed by a factory is derived from the author's method, so
`_GetAlphaAsync` used to produce a **property** called `GetAlphaAsyncCached`. A `Get` prefix
announces an operation, so it belongs on the members that really are methods.

Two rules, both in `GetResolverName`:

- A member that is not method-shaped never keeps a `^Get[A-Z0-9]` prefix; the first three
  characters are dropped. The pattern requires the fourth character to be upper case or a
  digit, so a factory named `_Gettysburg` keeps its name — "Get" there is a word, not a
  prefix. The backing field is derived after the trim, so `_alphaAsyncCached` and
  `AlphaAsyncCached` stay in step.
- An explicit `nameFormat` wins over the factory name. It used to be overwritten
  unconditionally, so the option was silently discarded for any registration carrying
  `source:`.

The trim is skipped when the author supplied `nameFormat`: a name they wrote is taken
literally.

#### The ladder, and why nothing bypasses it

`methodsRegistry` is what keeps two members from claiming the same name. It used to be fed
from one place only — the type-derived branch — and it reserved the name *before* the
decorations (`Cached`, `Async`, the `Get` prefix) were applied. So a name derived from a
factory or requested through `nameFormat` never entered it at all, and even a reserved name
was emitted in a shape nobody had reserved.

The `Get` trim widened that hole into a reproducible one: a factory named `_GetFar` now
resolves to `Far`, which is exactly the name a `[Transient<Far>]` claims, and the container
failed to compile with `CS0102: already contains a definition for 'Far'`.

Reservation now happens on the **final** name, on every route, and covers the backing field
too — a method `GetX` and a property `X` both derive `_x`, so checking only the member would
let a field collision through. Distinctions are added only when they are needed, cheapest
first:

| | Candidates, in order |
|---|---|
| No key | `{type}`, `{lifetime}{type}` |
| With key | `{type}`, `{type}{key}`, `{lifetime}{key}`, `{lifetime}{key}{type}` |
| Anything still taken | `…{n}`, counting from 1 |

`{lifetime}{type}` is deliberately absent from the keyed ladder. With it, two registrations of
the same type separated only by a key came out as `Db` and `SingletonDb`: the key vanished
from both names and declaration order decided which was which. Without it they are `Db` and
`DbWrite`.

Trying the type before the key is a behaviour change for keyed registrations, and it reads
better wherever the implementation types already differ: three `[Singleton<IService, X>("svc")]`
registrations used to be `Svc`, `SvcBeta`, `SvcGamma`, and are now `Alpha`, `Beta`, `Gamma`.

A name the author asked for is never renamed away — but it is numbered rather than duplicated,
so asking for the same `nameFormat` twice yields `Same` and `Same1` instead of a container that
does not compile. A diagnostic would be friendlier than a silent counter here, and is the
obvious follow-up.

#### `nameFormat` placeholders

Besides the historical `{0}`, which is the key, the format understands `{lifetime}`, `{key}`
and `{type}` (`{tipo}` is accepted as well):

```csharp
[Scoped<Thing>("main", nameFormat: "{lifetime}{key}{type}")]  // ScopedMainThing
[Scoped<Other>("aux",  nameFormat: "The{type}For{key}")]      // TheOtherForAux
```

Named placeholders are substituted before `string.Format` runs, and `string.Format` is only
invoked when `{0}` is actually present — it throws on a stray brace, and there is no longer any
reason to run that risk.

### Benchmarks

Measured against every actively maintained compile-time DI container, following the
scenarios of [.NET Matrix](https://github.com/DevTeam/dotnet-matrix), with hand-written
`new(...)` as the baseline. `net10.0`, Intel i9-14900HX, BenchmarkDotNet 0.15.8. Reproduce
with `dotnet run -c Release --project Benchmarks -- --filter "*"`.

**Read the allocation table first, and treat the timing table as approximate.** That is not
modesty, it is what repeated measurement showed: see [How much to trust
these numbers](#how-much-to-trust-these-numbers) below.

#### Allocations per operation

These are exact, and they reproduced byte-for-byte across every harness configuration tried.

| Scenario | Hand Coded | **SourceCrafter** | Jab | Pure.DI | StrongInject | MrMeeseeks.DIE |
|---|---|---|---|---|---|---|
| Resolve singleton | 0 B | **0 B** | 0 B | 0 B | 0 B | 136 B |
| Resolve transient | 80 B | **80 B** | 80 B | 80 B | 80 B | 168 B |
| Complex graph (15 nodes) | 424 B | **424 B** | 424 B | 424 B | 424 B | 560 B |
| Create scope, resolve, dispose | 24 B | **64 B** | 64 B | 152 B | n/a | n/a |
| Create scope, resolve nothing | 24 B | **40 B** | 40 B | 128 B | n/a | n/a |
| Create container + resolve | 48 B | **48 B** | 88 B | 216 B | 184 B | 1064 B |

Building object graphs — which is what a container spends its life doing — allocates exactly
what the equivalent hand-written `new(...)` allocates. The overhead is confined to scopes.

#### Timing

Ratio against `Hand Coded`; lower is better. Harness: 3 process launches, 10 warmup and 15
measured iterations each, with memory randomization enabled.

| Scenario | **SourceCrafter** | Jab | Pure.DI |
|---|---|---|---|
| Resolve singleton | **0.99x** | 0.70x † | 1.10x |
| Resolve transient | **0.96x** | 0.99x | 0.98x |
| Complex graph (15 nodes) | **1.01x** | 0.99x | 1.03x |
| Create scope, resolve, dispose | **tie with Jab** | tie | 4.2x slower |
| Create scope, resolve nothing | **1.28x** | 1.32x | 10.0x |
| Create container + resolve | **0.58x** | 2.79x | 6.55x |

† Jab's singleton figure is an artifact of the harness, not a result. It is explained below,
because the explanation is more interesting than the number.

The scope rows read "tie" on purpose: this container and Jab measured 13.47 ns and 13.37 ns,
a 0.7% gap against a `RatioSD` two orders of magnitude larger. Reporting that as a win in
either direction would be reporting noise.

The empty-scope row is a tie too (3.43 ns against Jab's 3.54 ns, both ±0.15). It is listed
separately only because it did *not* start out that way, and what closed it is described
next.

#### The singleton row, and why a faster number can mean slower code

Earlier revisions published `~1.5x` here against Pure.DI's `~1.0x`, which reads as a loss. It
never reproduced. Two re-measurements — one noisy, one clean — and a disassembly dump settle
what is actually going on. The clean run:

| | Mean | StdDev | Ratio | RatioSD | Code size |
|---|---|---|---|---|---|
| Hand Coded | 0.5421 ns | 0.0187 ns | 1.00 | 0.05 | 14 B |
| **SourceCrafter** | **0.5375 ns** | 0.0132 ns | **0.99** | 0.04 | *none* |
| Jab | 0.3800 ns | 0.0168 ns | 0.70 | 0.04 | 1,032 B |
| Pure.DI | 0.5934 ns | 0.0350 ns | 1.10 | 0.07 | 345 B |

**This container ties hand-written code exactly**, and the `Code size` column says why: there
is no machine code for it. The whole chain — property, field read, null test — inlines into the
caller and leaves nothing behind. `Hand Coded` itself, a `static readonly` field read, keeps a
14-byte body.

Which raises the obvious question: how is Jab *faster than the baseline*? It is not. Its hot
path, taken verbatim from the disassembly, is a full stack frame:

```asm
push rbp; push rsi; push rbx; sub rsp,30; lea rbp,[rsp+40]   ; prologue
mov  rbx,[rcx+10]          ; load the container field
cmp  [rbx],bl              ; null check
cmp  qword ptr [rbx+10],0  ; _IDatabase == null ?
je   M00_L01               ; ... slow path
mov  rax,[rbx+10]          ; load it
add  rsp,30; pop rbx; pop rsi; pop rbp; ret                  ; epilogue
```

Roughly a dozen instructions against hand-written code's three (`mov rax,[addr]; ret`). It
cannot be 30% faster; it is unambiguously slower. What happened is that **at 1,032 bytes Jab's
method is too fat for the JIT to inline**, so it stays a `call` — and BenchmarkDotNet
calibrates its overhead against an empty *call*. A method that is too big to inline gets a
whole call's worth of overhead subtracted from it; one that inlines fully, like this container's
and like the baseline, does not. Below a nanosecond that subtraction is the entire measurement.

So the ranking on this row is inverted with respect to the code: the fattest hot path posts the
lowest number. This is why the row is quoted with a dagger rather than as a win for anyone, and
it is a concrete instance of the rule stated below — except the failure mode here is not
variance, it is a *systematic* bias, which no amount of re-running would have exposed. Only the
disassembly did.

Two further caveats worth keeping:

- **The same scenario also produced a run with `StdDev` of 0.29–0.34 ns**, an order of
  magnitude worse than the table above, where the baseline itself measured 0.64 ns ± 0.29. The
  ordering held across both runs; only the magnitudes moved. Quote this row's *ratios*, never
  its absolute nanoseconds.
- The rows genuinely above the noise floor are StrongInject at 4.16 ns ± 0.04 and
  MrMeeseeks.DIE at 149 ns ± 4.2 — both tight, both real, and both explained by the lock
  schemes in the table further down.

`SingletonDisassemblyBenchmark` reproduces the dump above.

#### Why `Scoped` is sealed

This row used to read 1.51x against Jab's 1.38x, and the gap was not allocation — both
allocate exactly 40 B. It was `Scoped` being left as an ordinary `class`.

`Scoped` derives from the container and overrides `Root`, `CreateScope` and the disposer. With
the type open, the JIT cannot prove that a `Scoped` reference is not some further-derived type,
so those calls fall back to profile-guided *speculative* devirtualization: a guarded check that
sometimes hits and sometimes misses. `ScopeShapeBenchmark` measures the four shapes with
identical fields, so allocation is held constant and only dispatch varies:

| Shape | Mean | StdDev |
|---|---|---|
| Virtual `CreateScope`, **open** `Scoped` (before) | 7.94 ns | 1.898 ns |
| Virtual `CreateScope`, **sealed** `Scoped` (now) | 3.49 ns | 0.053 ns |
| Non-virtual `CreateScope`, sealed `Scoped` | 3.48 ns | 0.110 ns |
| Separate scope class (Jab's shape) | 3.19 ns | 0.055 ns |

Two things are worth reading off that table. **Sealing is worth 2.3x**, and it costs a
keyword — nothing can derive from a nested type the generator emits in full. And the standard
deviation collapsing from 1.898 ns to 0.053 ns is the actual tell: BenchmarkDotNet flagged the
open variant as multimodal (`mValue = 3`), which is exactly the signature of a guarded
devirtualization check landing differently across runs. The open shape was not merely slower,
it was *unpredictable*.

The third row is the useful negative result: **making `CreateScope` non-virtual buys nothing
once the type is sealed** (3.48 vs 3.49 ns). Propagating the root through `_root ?? this`
instead of an override — what Jab and Pure.DI both do — would have let the virtuals go
entirely, and it would have been wasted work. The remaining 0.3 ns against Jab is the
`_disposed` flag, which is a correctness feature this container has and Jab does not.

Scope allocations used to be 112 B here against Jab's 64 B. The gap was one 40 B
`System.Threading.Lock` per cached dependency. Sharing a single lock per lifetime — and
hoisting every lock-acquiring dependency out of the guarded region so that sharing stays
deadlock-free — closed it exactly, without adopting the deadlock described below.

StrongInject and MrMeeseeks.DIE have no request scope: the former models lifetime through
ownership (`Owned<T>`), the latter through creation-function-bound transient scopes. Inventing
an equivalent would produce a number no user of theirs could reproduce. StrongInject is also
doing strictly more work in every row, since `Run`/`Resolve` track ownership for deterministic
disposal. Their timings are omitted from the ratio table above because most of their rows were
not re-measured under the hardened harness; the singleton row is the exception, and it is the
one place where both are far enough above the noise floor to be quoted with confidence.

#### How much to trust these numbers

Less than a table of three-significant-digit ratios suggests. Earlier revisions of this file
published figures from a short-job run; re-measuring contradicted several of them, and
investigating the contradiction is what produced the harness described above.

Two findings are worth stating plainly, because they bound what any DI benchmark can claim:

- **The complex-graph scenario measured 0.95x, 1.01x, 1.02x and 2.10x for this container in
  four runs of the same binary, with identical 424 B allocations every time.** Allocation
  counts are a property of the code; sub-100 ns timings on this hardware are partly a property
  of the process that happened to run them.
- **In one single-launch run, all three containers jumped from ~38 ns to ~80 ns while the
  hand-coded baseline did not move.** Three independent libraries do not regress in lockstep
  to the same value; that was an artifact, and it is the reason the harness now averages
  across processes.
- **The singleton row published `~1.5x` for this container and re-measured at `0.99x`, tying
  hand-written code** — and in the same table Jab posts `0.70x`, faster than a baseline whose
  entire body is `mov rax,[addr]; ret`. That one is not variance but *systematic* bias, and
  re-running would never have caught it; see
  [the singleton row](#the-singleton-row-and-why-a-faster-number-can-mean-slower-code).

The rule this implies: **a difference whose `RatioSD` is comparable to the `Ratio` is not a
difference.** Anything within roughly ±10% in the timing table above should be read as a tie.

#### Thread-safety schemes

Read from each library's generated output, not from documentation. This is what the
allocation differences in the table above actually buy:

| | Lock granularity | Consequence |
|---|---|---|
| **SourceCrafter** | One per lifetime — `lock(this)` for scoped, one static lock for singletons — with every lock-acquiring dependency hoisted out of the guarded region | Free to allocate *and* deadlock-free: a resolver never holds one lock while acquiring another, so the wait-for graph has no edges |
| Jab | `lock(this)` on the root and on each scope | Free, but it is one-lock-per-lifetime *without* hoisting: a singleton depending on a scoped service and another thread resolving in the opposite order close a wait cycle |
| Pure.DI | One `Lock` per composition, *inherited* by child scopes | Correct — `lock` is reentrant — but every construction in the whole tree serializes on it |
| StrongInject | One `SemaphoreSlim` per container | Correct; a semaphore is heavier than a monitor and is not reentrant |
| MrMeeseeks.DIE | `SemaphoreSlim` plus `Interlocked` resolution counters | Explains the 136 B and 144 ns to hand back an already-built singleton |

Pure.DI independently makes the same `System.Threading.Lock`-when-available choice this
generator does, which is a good sign that the trade-off is the right one.

Hoisting is what separates row one from row two. Sharing a lock across a lifetime is only
safe if nothing inside the critical section can block on another lock, so the slow path
resolves its cached dependencies — and any transient whose subtree contains one — into locals
*before* taking the lock:

```csharp
private Session __Create_session()
{
    var __a0 = Config;            // acquires the singleton lock, then releases it
    var __a1 = new Wrapper(Clock); // transient, but Clock is cached — also hoisted

    lock(this)                     // nothing inside can acquire anything
    {
        return _session ??= new Session(__a0, __a1);
    }
}
```

`LockingStrategyTests` contains a test that reproduces the deadlock this avoids, and
`GeneratedCodeTests.CachedDependenciesAreResolvedBeforeTakingTheLock` asserts on the emitted
text that nothing which acquires a lock is left inside the guarded region.

The trade-offs this accepts, stated plainly: `lock(this)` leaves the lock publicly acquirable
by anyone holding the scope, and if two threads race into the same slow path they both hoist,
so a hoisted *transient* may be built twice and one copy discarded. That can only happen on
the very first resolution of a service, and transients are not tracked for disposal.

One difference in the table is not a lock-scheme decision:

- **`Create container + resolve` is not the same semantics across libraries.** Here
  singletons are `static`, so a fresh container reuses the one already built.

The fast path of a cached resolver reads the backing field into a local, and falls through to
a `[MethodImpl(NoInlining)]` slow path. Three measurements justify that shape, taken on
hand-written variants of the same already-built service (`ResolverShapeBenchmark`):

- **Having a fast path at all is worth 11x**: entering the lock on every resolve costs
  8.83 ns against 0.56 ns.
- **Moving the lock out of the getter is worth a further ~32%**: 0.83 ns with the `lock`
  inline versus 0.56 ns with it split out (ratio 0.68, RatioSD 0.04). A getter containing a
  `lock` is too big for the JIT to inline into the caller; a getter that is just a field
  read and a call is not.
- **The local costs nothing**: 0.5607 ns versus 0.5716 ns for reading the field twice — and it
  cuts the standard deviation from 1.067 to 0.018, because the JIT no longer gets to choose
  whether to rematerialize the second read. It also removes a real bug for value types, where
  two reads of a `Nullable<T>` can return one read's `HasValue` with another's `Value`.

The slow path is only ever entered on the first resolution, so it costs nothing per call
afterwards.

The **async** fast path used to be exempt from that rule, and it was a bug rather than an
optimisation. It read the field twice — `if(_f is { IsCompletedSuccessfully: true }) return _f;`
— while the disposer sets `_f = null`. A thread disposing between the two reads made a member
declared `Task<T>` hand back `null`, and its `ValueTask<T>?` variant throw from the `.Value`.
Roslyn's nullable analysis never flagged it because pattern matching a field narrows its flow
state on the assumption that nothing else writes it. The race is cheap to hit: a reader thread
against a disposer thread observed roughly 4·10⁶ nulls in three seconds, in Debug *and* in
Release, on the real generated container. All three cached shapes — property, `ValueTask`
property and method — now read into `__v` first, and `AsyncFastPathTests` covers both the
emitted text and the race.

One hazard the local does not remove: a `ValueTask<T>?` backing field is a multi-field struct,
so writing it is not atomic and a concurrent reader can in principle observe a torn copy. The
local narrows this to the copy itself instead of spreading it across the null check and the
unwrap, but closing it entirely would mean publishing a reference instead of the struct.

#### What Pure.DI does differently, and what was worth taking

The disassembly above also answers "what makes Pure.DI fast at singletons" better than any
ratio could: at 345 bytes its root does not inline either, and it still measures 1.10x. Its
generated singleton root is:

```csharp
public global::Benchmarks.IDatabase Database
{
    [MethodImpl((MethodImplOptions)256)]   // AggressiveInlining
    get
    {
        var root249d = _root249d ?? this;
        if (root249d._singletonDatabase249d is null)
            lock (_lock249d)
                if (root249d._singletonDatabase249d is null)
                    root249d._singletonDatabase249d = new Database(new Settings());

        return root249d._singletonDatabase249d;
    }
}
```

Three differences from what this generator emits, and what each one is actually worth:

- **The singleton lives in an instance field reached through `_root`, not in a `static`
  field.** That is the real reason their `Create container + resolve` row is 6.55x while this
  one is 0.58x: theirs genuinely rebuilds per composition, this one does not. It is a semantic
  difference, not an optimization, and it is already called out above. Worth knowing, not
  worth copying — but it does mean the two singleton rows are not measuring the same thing.
- **`[AggressiveInlining]` on the getter, with the `lock` left inline.** This container does
  the opposite: keep the getter tiny and push the `lock` into a `NoInlining` method. The
  measurement above (0.83 ns inline versus 0.56 ns split) says the split shape wins, so this
  was not adopted. Their getter also re-reads the field after the lock, which is the
  double-read this generator deliberately avoids.
- **Disposables go into a pre-sized `object[]` with a bump index** (`_disposables[_i++]`),
  sized at generation time. Neat, and strictly better than a `List<object>` — but this
  generator tracks disposables in their own typed fields and emits a straight-line disposer,
  which allocates nothing at all. Their array is also why a Pure.DI scope costs 128 B and
  26.9 ns: every scope constructor allocates one, resolved or not.

So the honest answer on the singleton row is that there is **no technique here left to
borrow**: this container already inlines to nothing and ties hand-written code, while Pure.DI's
root keeps a 345-byte body. A separate experiment (`SingletonStorageBenchmark`) tried to
attribute the old `1.5x` to this container storing singletons in `static` fields — the theory
being that the `EnvironmentName` initializer gives the class a `.cctor` and so a
class-initialization check on every read. **The experiment refuted it twice over**: the
variant *with* the initializer measured faster than the one without (0.68 ns against 1.59 ns,
which is backwards), and the "without" class turned out to still have a `.cctor` anyway,
because `static readonly Lock __singletonLock = new()` is itself a static initializer. Removing
`EnvironmentName` would not have removed the `.cctor`, so the proposed fix could not have
worked. It is recorded here because a plausible, wrong optimization is worth exactly one
measurement to kill.

One Pure.DI detail is unrelated to speed and worth explaining: every generated field carries
`[NonSerialized]`. That is what "serialization" refers to in a Pure.DI context — not
serializing the object graph, but making sure that if a user marks their own partial
composition class `[Serializable]`, the container's internal state (cached singletons, locks,
the root back-reference) is not dragged into the payload. See
[why we do not copy it](#not-adopted-nonserialized).

#### Not adopted: `[NonSerialized]`

Deliberately skipped. `[NonSerialized]` only affects `BinaryFormatter`, which is removed in
.NET 9+, and the fields here are private. Adding an attribute to every field to guard against
a mechanism that no longer exists is cost without benefit.

There is a real risk nearby that this attribute would *not* have covered: pointing a
public-property serializer such as `System.Text.Json` at a container would **invoke every
resolver**, because each service is exposed as a property. That needs `[JsonIgnore]`, not
`[NonSerialized]`, and it will be addressed if a concrete case turns up.

---

## Generated Code Example

For this container:

```csharp
[ServiceProvider]
[Singleton<IClock, SystemClock>]
[Scoped<DbSession>]
[Transient<Handler>]
public partial class AppContainer { }
```

the generator emits the following — this is real, verbatim output:

```csharp
// <auto-generated/>
#nullable enable
namespace MyApp;

[global::System.CodeDom.Compiler.GeneratedCode("SourceCrafter.DependencyInjection", "<version>")]
public partial class AppContainer : global::System.IAsyncDisposable
{
	public const string EnvironmentVariableName = "DOTNET_ENVIRONMENT";

	public static string EnvironmentName { get; } = global::System.Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? "Development";

	private bool _disposed = false;

	private static readonly global::System.Threading.Lock __singletonLock = new();

	private static global::MyApp.SystemClock? _systemClock;
	[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private global::MyApp.SystemClock __Create_systemClock()
	{
		lock(__singletonLock)
		{
			return _systemClock ??= new global::MyApp.SystemClock();
		}
	}

	public global::MyApp.IClock SystemClock
	{
		get
		{
			var __v = _systemClock;

			if(__v is not null) return __v;

			return __Create_systemClock();
		}
	}

	private global::MyApp.DbSession? _dbSession;
	[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private global::MyApp.DbSession __Create_dbSession()
	{
		var __a0 = SystemClock;

		lock(this)
		{
			return _dbSession ??= new global::MyApp.DbSession(
				__a0);
		}
	}

	public global::MyApp.DbSession DbSession
	{
		get
		{
			var __v = _dbSession;

			if(__v is not null) return __v;

			return __Create_dbSession();
		}
	}

	public global::MyApp.Handler Handler
		=> new global::MyApp.Handler(
				DbSession);

	private AppContainer _root = default!;

	public virtual AppContainer Root => this;

	public virtual Scoped CreateScope() => new() { _root = this };

	public sealed class Scoped : AppContainer
	{
		public override AppContainer Root => _root;

		public override Scoped CreateScope() => new() { _root = _root };

		public override global::System.Threading.Tasks.ValueTask DisposeAsync()
		{
			if (_disposed) return default;

			_disposed = true;

			return ScopedDisposeAsync();
		}
	}

	private global::System.Threading.Tasks.ValueTask ScopedDisposeAsync()
	{
		{
			var __disposing = _dbSession;
			_dbSession = null;
			return __disposing?.DisposeAsync() ?? default;
		}
	}

	public virtual global::System.Threading.Tasks.ValueTask DisposeAsync()
	{
		if (_disposed) return default;

		_disposed = true;

		return ScopedDisposeAsync();
	}

}
```

Several things in that output are deliberate and worth calling out:

- **Each lock has the same scope as the field it protects.** `_systemClock` is `static`, so
  its lock is the `static` `__singletonLock`. A `lock(this)` guarding a `static` field would
  give no mutual exclusion at all — two container instances would happily build the same
  singleton twice.
- **One lock per lifetime, made safe by hoisting.** `_dbSession` is per-instance, so the
  scope itself serves as its lock: `lock(this)` allocates nothing. On its own that is the
  scheme that deadlocks — a singleton may depend on a scoped service and vice versa, so one
  thread takes the singleton lock then wants the scope while another does the exact opposite.
  What removes the cycle is the line above the lock: `var __a0 = SystemClock;`. Every
  dependency that acquires a lock — cached ones, and transients whose subtree contains one —
  is resolved into a local *before* the lock is taken, so a resolver never holds one lock
  while acquiring another. The alternative, a lock per cached dependency, is also correct but
  costs 40 bytes per resolved service; measured over a scope's create-resolve-dispose cycle,
  hoisting cut 192 B to 96 B and 43.3 ns to 30.6 ns. `LockingStrategyTests` demonstrates each
  scheme in isolation, including the deadlock that hoisting avoids.
- **Locks are typed `System.Threading.Lock` when the target project can use it** (C# 13 and
  a runtime that ships the type), falling back to `object` otherwise. The dedicated type
  avoids the object header's sync block and lets the compiler emit `EnterScope` instead of
  `Monitor`. Emitting it under C# 12 would be counterproductive: the compiler would convert
  it back to `object`, fall back to `Monitor` anyway, and warn (CS9216).
- **Cached *asynchronous* resolvers keep a lazily created lock per dependency.** Their bodies
  still start tasks inside the guarded region, so they do not satisfy the hoisting condition.
  Mixing the two schemes is safe precisely because the synchronous side hoists: whoever holds
  `this` is not waiting on anything else, so no cycle can close.
- **Those locks are created through a shared `__EnsureLock` helper.** It uses
  `Interlocked.CompareExchange` rather than `??=`, which expands to read-check-write and is
  not atomic: two threads could end up with different locks and therefore no mutual exclusion
  at all. The helper lives in a single generated file per compilation (`Locks.g.cs`) and each
  container reaches it through `using static`. Emitting it inside every container duplicated an
  identical body — the lock type is decided per *compilation*, not per container — and put
  twelve lines of plumbing at the head of every generated file. It is deliberately **not**
  generic: generics only specialize per type for value types, so with reference types the
  shared canonical body cannot emit `newobj` and `new T()` becomes
  `Activator.CreateInstance<T>()` (measured 18–20 ms against 13–14 ms over two million
  creations), and it would buy nothing anyway, since `object` and `Lock` never coexist in one
  compilation.
- **The cached fast path reads the field into a local; the `lock` lives in a separate
  `[MethodImpl(NoInlining)]` method.** With the `lock` inside the getter, the getter carries a
  protected region and the JIT stops inlining it — 0.83 ns against 0.56 ns once split. The
  local is free and removes a real bug: two separate reads of a `Nullable<T>` backing field
  can return one read's `HasValue` with another's `Value`. That slow-path method is never
  `static`, even for singletons: building a singleton may touch instance resolvers.
- **A constructor is emitted only when a resolver consumes the lifetime cancellation token**,
  which is the only remaining thing that needs initializing. Otherwise the parameterless
  constructor stays yours.
- **`_disposed` is initialized at its declaration**, not in the constructor.
- **`Root` and `CreateScope()` are `virtual`/`override`, and `Scoped` is `sealed`.** Resolver
  bodies are declared once on the container and inherited by `Scoped`. Without virtual
  dispatch, a `[Root]` parameter injected from a scope would bind at compile time to
  `Root => this` and receive the scope instead of the root. Sealing costs nothing — nothing
  can derive from a nested type the generator emits in full — and buys back most of what the
  virtuals cost: see [Why `Scoped` is sealed](#why-scoped-is-sealed).
- **Disposers null the field before releasing it**, and run in reverse construction order.
  Transients are never tracked — you own their lifetime.

---

---

## Cancellation: why the caller's token is never forwarded

Factory methods may take a `CancellationToken`:

```csharp
[Singleton("count", source: nameof(LoadAsync))]
static Task<int> LoadAsync(CancellationToken token) => /* ... */;
```

The generator never forwards the *caller's* token. A cached value is handed to every
later caller, so recording the first caller's token inside it would be wrong. Instead the
container owns a single `CancellationTokenSource`, copies its token once into a `readonly`
field, and cancels + disposes the source when the container is disposed:

```csharp
private readonly global::System.Threading.CancellationTokenSource __lifetimeCts;
private readonly global::System.Threading.CancellationToken __lifetimeToken;
```

The token is copied into a field rather than read from `__lifetimeCts.Token` on every
access because `CancellationTokenSource.Token` throws `ObjectDisposedException` once the
source is disposed — which is exactly what disposal does, while resolvers may still be
invoked. A `CancellationToken` is a struct wrapping a reference, so the copy allocates
nothing.

Each scope is its own container instance, so each scope gets its own source: cancellation
is per-scope for free. A container that uses the token becomes disposable even when none
of its services are, because the source itself has to be released.

Because of that, **no generated member accepts a `CancellationToken`** — not the resolvers,
not the interceptors, not the generic `GetRequired*Async<T>()` surface. A parameter that is
silently ignored is worse than no parameter at all, so it is not emitted:

```csharp
// emitted
public Task<TOut> GetRequiredServiceAsync<TOut>() where TOut : notnull => ...;

// not emitted
public Task<TOut> GetRequiredServiceAsync<TOut>(CancellationToken token = default) ...
```
