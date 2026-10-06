# ADR 0001: C# runtime, browser editor, command bus, golden replays

Status: accepted (first slice)

## Decision
- The game runtime is C#. It is compiled to WebAssembly for the in-browser editor and playtests, and
  with NativeAOT for shipped builds (Windows, macOS, Android, iOS). The web build is never released.
- The editor is a browser app that talks to the runtime only through a command bus with JSON
  arguments and results. The same bus will serve the console, agents (MCP) and a phone over WebSocket.
- The simulation is deterministic at a fixed 60 Hz tick. Runs are recorded as inputs plus live tuning
  changes and verified as golden replays, bit for bit, in C# and in a JS reference implementation.

## Why
- C# is the main language; NativeAOT keeps full speed on iOS where JIT is banned.
- One command bus keeps every frontend consistent and makes undo, history and recording free.
- Bit-exact replays turn "the browser build behaves like the native build" into a test, not a hope.

## Consequences
- Rendering in the browser is three.js for now; native rendering (Sokol) is the next host to build.
- `Sim.cs` must follow strict determinism rules (see AGENTS.md).
- The JS reference sim doubles as the editor fallback and as a second oracle for the C# sim.

## Kill criterion
If the WebAssembly or NativeAOT builds cannot be made to work on the target platforms within two
weekends of local effort, switch the runtime to Defold and keep the editor, content format and replays.
