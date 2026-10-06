// StackS native platform layer: window, input, and a minimal PS1 draw backend on top of Sokol.
// All game logic lives in C#. This layer only opens a window, forwards input, and draws boxes.
#pragma once
#include <stdint.h>

#if defined(_WIN32)
  #define SP_API __declspec(dllexport)
#else
  #define SP_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum {
    SP_EVENT_KEY_DOWN = 1, SP_EVENT_KEY_UP = 2,
    SP_EVENT_TOUCH_BEGIN = 3, SP_EVENT_TOUCH_MOVE = 4, SP_EVENT_TOUCH_END = 5, SP_EVENT_TOUCH_CANCEL = 6,
    SP_EVENT_RESIZED = 7, SP_EVENT_FOCUS_LOST = 8,
};
// Key codes are the sokol_app codes (GLFW-compatible): SPACE=32, A=65, D=68, S=83, W=87, K=75,
// RIGHT=262, LEFT=263, DOWN=264, UP=265, TAB=258, ESCAPE=256, P=80.

typedef struct sp_touch { uint64_t id; float x, y; int32_t changed; int32_t _pad; } sp_touch;

typedef struct sp_event {
    int32_t type;
    int32_t key;
    int32_t num_touches;
    int32_t width, height;
    int32_t _pad;
    sp_touch touches[4];
} sp_event;

typedef struct sp_callbacks {
    void (*init)(void);
    void (*frame)(void);
    void (*event)(const sp_event* ev);
    void (*cleanup)(void);
    int32_t width, height;
    const char* title;
} sp_callbacks;

// Desktop entry point: runs the app loop until the window closes (built with SP_NO_ENTRY).
SP_API void sp_run(const sp_callbacks* cb);

SP_API double sp_frame_duration(void);
SP_API int sp_width(void);
SP_API int sp_height(void);
SP_API void sp_quit(void);

// Draw API, valid only inside the frame callback.
// fog_rgb: 3 floats. ps1: 1 = low-res + jitter + affine + dither. low_res_height: e.g. 240.
SP_API void sp_begin(const float* fog_rgb, float fog_near, float fog_far, int32_t ps1, int32_t low_res_height);
// mvp, mv: 16 floats each (System.Numerics.Matrix4x4 memory layout). Unit cube scaled/rotated by the caller.
SP_API void sp_box(const float* mvp, const float* mv, float r, float g, float b, float yaw_cos, float yaw_sin, float tex_repeat);
SP_API void sp_end(void);

#if !defined(SP_NO_ENTRY)
// Mobile builds: implemented by the C# game library (NativeAOT export) and called from sokol_main.
void stacks_configure(sp_callbacks* cb);
#endif

#ifdef __cplusplus
}
#endif
