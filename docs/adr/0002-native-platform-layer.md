# ADR 0002: a thin C platform layer on Sokol, driven by C#

Status: accepted

## Decision
- Native builds use one small C file, `native/stacks_platform.c`, on top of sokol_app and sokol_gfx.
  It opens the window, forwards input as a fixed struct, and offers a tiny draw API
  (`sp_begin`, `sp_box`, `sp_end`) that renders the PS1 look (low-res target, vertex snapping,
  affine textures, vertex lighting, fog, dithering) and upscales with nearest filtering.
- All decisions (simulation, camera, what to draw, input mapping) live in C# (`src/StackS.Game`).
- Shaders are written once (`native/ps1.glsl`) and compiled by sokol-shdc for GL 4.1, GLES3,
  D3D11, Metal macOS/iOS/simulator. The generated header is checked in.
- Entry points: desktop calls `sp_run` from C# (`SOKOL_NO_ENTRY`); Android and iOS enter through
  `sokol_main`, which calls `stacks_configure`, exported by the NativeAOT-compiled C# library.

## Why
- Full C# bindings for sokol_gfx are large and churn with Sokol's API; a 5-function draw API does not.
- Keeps the native code small enough to read in one sitting, which matters for maintainability.
- One draw API serves all platforms; backends differ only in the Sokol defines.

## Consequences
- New visual features need a C change plus a C# change. Keep the C API dumb and data-driven.
- Sokol headers are vendored (`native/third_party`); update deliberately and recompile shaders.
