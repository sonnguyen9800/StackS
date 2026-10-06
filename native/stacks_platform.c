// StackS native platform layer. See stacks_platform.h.
// Backend: Windows D3D11, macOS/iOS Metal, Android GLES3, Linux GL 4.1 core.
#if defined(SP_NO_ENTRY)
  #define SOKOL_NO_ENTRY
#endif
#if defined(_WIN32)
  #define SOKOL_D3D11
#elif defined(__APPLE__)
  #define SOKOL_METAL
#elif defined(__ANDROID__)
  #define SOKOL_GLES3
#else
  #define SOKOL_GLCORE
#endif
#define SOKOL_IMPL
#include "third_party/sokol_log.h"
#include "third_party/sokol_app.h"
#include "third_party/sokol_gfx.h"
#include "third_party/sokol_glue.h"
#include "ps1.glsl.h"
#include "stacks_platform.h"
#include <string.h>

static sp_callbacks g_cb;
static struct {
    sg_pipeline pip, blit_pip;
    sg_buffer cube_vbuf, cube_ibuf, quad_vbuf;
    sg_image tex_img; sg_view tex_view; sg_sampler nearest_repeat, nearest_clamp;
    sg_image rt_color, rt_depth; sg_view rt_color_att, rt_depth_att, rt_tex; int rt_w, rt_h;
    float fog_rgb[3]; float fog_near, fog_far; int ps1; int in_pass;
} S;

// ---------- resources ----------
static void make_cube(void) {
    // 24 vertices: pos(3) nrm(3) uv(2); unit cube centred on the origin
    static const float n[6][3] = {{1,0,0},{-1,0,0},{0,1,0},{0,-1,0},{0,0,1},{0,0,-1}};
    float v[24 * 8]; uint16_t idx[36];
    for (int f = 0; f < 6; f++) {
        float nx = n[f][0], ny = n[f][1], nz = n[f][2];
        // two tangent axes for this face
        float ux = ny != 0 ? 1 : (nx != 0 ? 0 : 1), uy = 0, uz = nx != 0 ? 1 : 0;
        if (nz != 0) { ux = 1; uz = 0; }
        float vx = ny * uz - nz * uy, vy = nz * ux - nx * uz, vz = nx * uy - ny * ux;
        static const float c[4][2] = {{-1,-1},{1,-1},{1,1},{-1,1}};
        for (int k = 0; k < 4; k++) {
            float* p = &v[(f * 4 + k) * 8];
            p[0] = 0.5f * (nx + c[k][0] * ux + c[k][1] * vx);
            p[1] = 0.5f * (ny + c[k][0] * uy + c[k][1] * vy);
            p[2] = 0.5f * (nz + c[k][0] * uz + c[k][1] * vz);
            p[3] = nx; p[4] = ny; p[5] = nz;
            p[6] = (c[k][0] + 1) * 0.5f; p[7] = (c[k][1] + 1) * 0.5f;
        }
        uint16_t b = (uint16_t)(f * 4);
        uint16_t* q = &idx[f * 6];
        q[0] = b; q[1] = b + 1; q[2] = b + 2; q[3] = b; q[4] = b + 2; q[5] = b + 3;
    }
    S.cube_vbuf = sg_make_buffer(&(sg_buffer_desc){ .data = SG_RANGE(v), .label = "cube-vertices" });
    S.cube_ibuf = sg_make_buffer(&(sg_buffer_desc){ .usage.index_buffer = true, .data = SG_RANGE(idx), .label = "cube-indices" });
    static const float quad[] = { 0,0, 1,0, 1,1, 0,0, 1,1, 0,1 };
    S.quad_vbuf = sg_make_buffer(&(sg_buffer_desc){ .data = SG_RANGE(quad), .label = "blit-quad" });
}

static void make_texture(void) {
    // 16x16 grey checker with deterministic noise: tinted per box by the shader
    uint32_t px[16 * 16]; uint32_t seed = 7;
    for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) {
        seed = seed * 1103515245u + 12345u;
        int c = (((x >> 2) + (y >> 2)) & 1 ? 232 : 182) + (int)((seed >> 16) % 22) - 11;
        c = c < 0 ? 0 : c > 255 ? 255 : c;
        px[y * 16 + x] = 0xFF000000u | ((uint32_t)c << 16) | ((uint32_t)c << 8) | (uint32_t)c;
    }
    S.tex_img = sg_make_image(&(sg_image_desc){ .width = 16, .height = 16, .pixel_format = SG_PIXELFORMAT_RGBA8,
        .data.mip_levels[0] = SG_RANGE(px), .label = "checker" });
    S.tex_view = sg_make_view(&(sg_view_desc){ .texture.image = S.tex_img });
    S.nearest_repeat = sg_make_sampler(&(sg_sampler_desc){ .min_filter = SG_FILTER_NEAREST, .mag_filter = SG_FILTER_NEAREST,
        .wrap_u = SG_WRAP_REPEAT, .wrap_v = SG_WRAP_REPEAT });
    S.nearest_clamp = sg_make_sampler(&(sg_sampler_desc){ .min_filter = SG_FILTER_NEAREST, .mag_filter = SG_FILTER_NEAREST,
        .wrap_u = SG_WRAP_CLAMP_TO_EDGE, .wrap_v = SG_WRAP_CLAMP_TO_EDGE });
}

static void make_pipelines(void) {
    S.pip = sg_make_pipeline(&(sg_pipeline_desc){
        .shader = sg_make_shader(ps1_shader_desc(sg_query_backend())),
        .layout.attrs = {
            [ATTR_ps1_pos].format = SG_VERTEXFORMAT_FLOAT3,
            [ATTR_ps1_nrm].format = SG_VERTEXFORMAT_FLOAT3,
            [ATTR_ps1_texcoord0].format = SG_VERTEXFORMAT_FLOAT2,
        },
        .index_type = SG_INDEXTYPE_UINT16,
        .cull_mode = SG_CULLMODE_NONE,
        .depth = { .compare = SG_COMPAREFUNC_LESS_EQUAL, .write_enabled = true, .pixel_format = SG_PIXELFORMAT_DEPTH },
        .colors[0].pixel_format = SG_PIXELFORMAT_RGBA8,
        .sample_count = 1,
        .label = "ps1-pipeline",
    });
    S.blit_pip = sg_make_pipeline(&(sg_pipeline_desc){
        .shader = sg_make_shader(blit_shader_desc(sg_query_backend())),
        .layout.attrs[ATTR_blit_bpos].format = SG_VERTEXFORMAT_FLOAT2,
        .label = "blit-pipeline",
    });
}

static void ensure_render_target(int w, int h) {
    if (w == S.rt_w && h == S.rt_h) return;
    if (S.rt_w) {
        sg_destroy_view(S.rt_color_att); sg_destroy_view(S.rt_depth_att); sg_destroy_view(S.rt_tex);
        sg_destroy_image(S.rt_color); sg_destroy_image(S.rt_depth);
    }
    S.rt_w = w; S.rt_h = h;
    S.rt_color = sg_make_image(&(sg_image_desc){ .usage.color_attachment = true, .width = w, .height = h,
        .pixel_format = SG_PIXELFORMAT_RGBA8, .sample_count = 1, .label = "low-res-color" });
    S.rt_depth = sg_make_image(&(sg_image_desc){ .usage.depth_stencil_attachment = true, .width = w, .height = h,
        .pixel_format = SG_PIXELFORMAT_DEPTH, .sample_count = 1, .label = "low-res-depth" });
    S.rt_color_att = sg_make_view(&(sg_view_desc){ .color_attachment.image = S.rt_color });
    S.rt_depth_att = sg_make_view(&(sg_view_desc){ .depth_stencil_attachment.image = S.rt_depth });
    S.rt_tex = sg_make_view(&(sg_view_desc){ .texture.image = S.rt_color });
}

// ---------- draw API ----------
SP_API void sp_begin(const float* fog_rgb, float fog_near, float fog_far, int32_t ps1, int32_t low_res_height) {
    int fw = sapp_width(), fh = sapp_height();
    if (fw < 1) fw = 1; if (fh < 1) fh = 1;
    int h = ps1 ? (low_res_height > 0 ? low_res_height : 240) : fh;
    int w = ps1 ? (int)((float)h * (float)fw / (float)fh + 0.5f) : fw;
    if (w < 1) w = 1;
    ensure_render_target(w, h);
    memcpy(S.fog_rgb, fog_rgb, sizeof(S.fog_rgb));
    S.fog_near = fog_near; S.fog_far = fog_far; S.ps1 = ps1;
    sg_begin_pass(&(sg_pass){
        .action.colors[0] = { .load_action = SG_LOADACTION_CLEAR, .clear_value = { fog_rgb[0], fog_rgb[1], fog_rgb[2], 1.0f } },
        .attachments = { .colors[0] = S.rt_color_att, .depth_stencil = S.rt_depth_att },
        .label = "scene",
    });
    sg_apply_pipeline(S.pip);
    fs_params_t fsp = { .fog_color = { fog_rgb[0], fog_rgb[1], fog_rgb[2], ps1 ? 1.0f : 0.0f } };
    sg_apply_uniforms(UB_fs_params, &SG_RANGE(fsp));
    S.in_pass = 1;
}

SP_API void sp_box(const float* mvp, const float* mv, float r, float g, float b, float yaw_cos, float yaw_sin, float tex_repeat) {
    if (!S.in_pass) return;
    sg_apply_bindings(&(sg_bindings){
        .vertex_buffers[0] = S.cube_vbuf, .index_buffer = S.cube_ibuf,
        .views[VIEW_tex] = S.tex_view, .samplers[SMP_smp] = S.nearest_repeat,
    });
    vs_params_t p;
    memcpy(p.mvp, mvp, sizeof(p.mvp));
    memcpy(p.mv, mv, sizeof(p.mv));
    p.color[0] = r; p.color[1] = g; p.color[2] = b; p.color[3] = 1;
    p.rot_rep[0] = yaw_cos; p.rot_rep[1] = yaw_sin; p.rot_rep[2] = tex_repeat; p.rot_rep[3] = 0;
    p.res_flags[0] = (float)S.rt_w; p.res_flags[1] = (float)S.rt_h;
    p.res_flags[2] = S.ps1 ? 1.0f : 0.0f; p.res_flags[3] = S.ps1 ? 1.0f : 0.0f;
    p.fog[0] = S.fog_near; p.fog[1] = S.fog_far; p.fog[2] = 0; p.fog[3] = 0;
    sg_apply_uniforms(UB_vs_params, &SG_RANGE(p));
    sg_draw(0, 36, 1);
}

SP_API void sp_end(void) {
    if (!S.in_pass) return;
    sg_end_pass();
    S.in_pass = 0;
    sg_begin_pass(&(sg_pass){ .action.colors[0] = { .load_action = SG_LOADACTION_CLEAR, .clear_value = { 0, 0, 0, 1 } },
        .swapchain = sglue_swapchain(), .label = "blit" });
    sg_apply_pipeline(S.blit_pip);
    sg_apply_bindings(&(sg_bindings){ .vertex_buffers[0] = S.quad_vbuf, .views[VIEW_btex] = S.rt_tex, .samplers[SMP_bsmp] = S.nearest_clamp });
    sg_backend be = sg_query_backend();
    blit_params_t bp = { .flip = { (be == SG_BACKEND_GLCORE || be == SG_BACKEND_GLES3) ? 0.0f : 1.0f, 0, 0, 0 } };
    sg_apply_uniforms(UB_blit_params, &SG_RANGE(bp));
    sg_draw(0, 6, 1);
    sg_end_pass();
    sg_commit();
}

SP_API double sp_frame_duration(void) { return sapp_frame_duration(); }
SP_API int sp_width(void) { return sapp_width(); }
SP_API int sp_height(void) { return sapp_height(); }
SP_API void sp_quit(void) { sapp_request_quit(); }

// ---------- app callbacks ----------
static void on_init(void) {
    sg_setup(&(sg_desc){ .environment = sglue_environment(), .logger.func = slog_func });
    make_cube(); make_texture(); make_pipelines();
    if (g_cb.init) g_cb.init();
}
static void on_frame(void) { if (g_cb.frame) g_cb.frame(); }
static void on_cleanup(void) { if (g_cb.cleanup) g_cb.cleanup(); sg_shutdown(); }
static void on_event(const sapp_event* e) {
    sp_event ev; memset(&ev, 0, sizeof(ev));
    ev.width = e->framebuffer_width; ev.height = e->framebuffer_height; ev.key = (int32_t)e->key_code;
    switch (e->type) {
        case SAPP_EVENTTYPE_KEY_DOWN: if (e->key_repeat) return; ev.type = SP_EVENT_KEY_DOWN; break;
        case SAPP_EVENTTYPE_KEY_UP: ev.type = SP_EVENT_KEY_UP; break;
        case SAPP_EVENTTYPE_TOUCHES_BEGAN: ev.type = SP_EVENT_TOUCH_BEGIN; break;
        case SAPP_EVENTTYPE_TOUCHES_MOVED: ev.type = SP_EVENT_TOUCH_MOVE; break;
        case SAPP_EVENTTYPE_TOUCHES_ENDED: ev.type = SP_EVENT_TOUCH_END; break;
        case SAPP_EVENTTYPE_TOUCHES_CANCELLED: ev.type = SP_EVENT_TOUCH_CANCEL; break;
        case SAPP_EVENTTYPE_RESIZED: ev.type = SP_EVENT_RESIZED; break;
        case SAPP_EVENTTYPE_UNFOCUSED: case SAPP_EVENTTYPE_SUSPENDED: ev.type = SP_EVENT_FOCUS_LOST; break;
        default: return;
    }
    ev.num_touches = e->num_touches > 4 ? 4 : e->num_touches;
    for (int i = 0; i < ev.num_touches; i++) {
        ev.touches[i].id = (uint64_t)e->touches[i].identifier;
        ev.touches[i].x = e->touches[i].pos_x; ev.touches[i].y = e->touches[i].pos_y;
        ev.touches[i].changed = e->touches[i].changed ? 1 : 0;
    }
    if (g_cb.event) g_cb.event(&ev);
}

static sapp_desc make_desc(void) {
    return (sapp_desc){
        .init_cb = on_init, .frame_cb = on_frame, .cleanup_cb = on_cleanup, .event_cb = on_event,
        .width = g_cb.width > 0 ? g_cb.width : 1280, .height = g_cb.height > 0 ? g_cb.height : 720,
        .window_title = g_cb.title ? g_cb.title : "StackS",
        .high_dpi = true,
        .logger.func = slog_func,
    };
}

#if defined(SP_NO_ENTRY)
SP_API void sp_run(const sp_callbacks* cb) {
    g_cb = *cb;
    sapp_desc d = make_desc();
    sapp_run(&d);
}
#else
SP_API void sp_run(const sp_callbacks* cb) { (void)cb; /* mobile builds enter through sokol_main */ }
sapp_desc sokol_main(int argc, char* argv[]) {
    (void)argc; (void)argv;
    stacks_configure(&g_cb);   // provided by the C# game library (NativeAOT)
    return make_desc();
}
#endif
