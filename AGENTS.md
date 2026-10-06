# Rules for agents working on StackS

Read this before every task. A correction made twice becomes a rule here, a test, or a hook.

## Architecture
1. `src/StackS.Kernel` depends on nothing but the .NET base library. Hosts (`StackS.Web`, `StackS.Cli`,
   future `StackS.Desktop`, `StackS.Android`) depend on the kernel, never the other way round.
2. Every change to a level goes through a named command on the command bus (`Runtime.Exec`).
   The editor never mutates level data directly. New features add commands, not side doors.
3. Level data (`LevelDoc`, `Part`, `Tuning`) is plain data: no behaviour, no engine references.
4. Play never mutates the level document. Play builds a runtime world from a clone; stop discards it.

## Determinism (checked by golden replays, bit for bit)
5. `Sim.cs` uses only `+ - * /` and `Math.Abs`. No `Math.Sin/Cos/Exp/Pow`, no `Random`, no clock,
   no dictionary iteration, no parallelism.
6. `Sim.cs` and `editor/sim.js` stay operation-for-operation identical, including evaluation order.
   Any change to one is made to the other in the same commit; regenerate goldens only when the
   behaviour change is intentional and say so in the commit message.
7. `Sim.Step` allocates nothing (a test asserts zero bytes over 600 ticks).

## NativeAOT
8. No reflection-based serialization or discovery. JSON uses the source-generated contexts in
   `Serializer.cs`. AOT/trim warnings are build errors (see `Directory.Build.props`).

## Native platform layer
11. `native/stacks_platform.c` stays dumb: window, input, drawing. No game rules in C.
12. Changing `native/ps1.glsl` means regenerating `native/ps1.glsl.h` with sokol-shdc
    (`-l glsl410:glsl300es:hlsl5:metal_macos:metal_ios:metal_sim`) in the same commit.
13. C structs in `stacks_platform.h` and their mirrors in `src/StackS.Game/Native.cs` change together.

## Files
9. Level files carry `version`. A format change bumps it and adds one migration step plus a fixture test.
10. Part ids are never reused (`nextId` only grows).

## Workflow
- Spec first (goal, interface, acceptance tests, what must not change), then a plan the human reviews.
- Before finishing: `dotnet run --project tests/StackS.Tests -c Release` and `node tools/check-js-goldens.mjs` pass.
