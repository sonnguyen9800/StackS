// PS1-style shaders for the StackS native renderer.
// Compile with sokol-shdc (see native/README.md); the output ps1.glsl.h is checked in.

@vs vs
layout(binding=0) uniform vs_params {
    mat4 mvp;
    mat4 mv;
    vec4 color;      // rgb, a unused
    vec4 rot_rep;    // cos(yaw), sin(yaw), texture repeat, unused
    vec4 res_flags;  // low-res width, height, jitter on/off, affine on/off
    vec4 fog;        // near, far, unused, unused
};
in vec3 pos;
in vec3 nrm;
in vec2 texcoord0;
out vec2 uv_w;
out float w_out;
out vec3 shade;
out float fog_f;

void main() {
    vec4 clip = mvp * vec4(pos, 1.0);
    if (res_flags.z > 0.5) {
        vec2 g = res_flags.xy * 0.5;
        clip.xy = floor(clip.xy / clip.w * g + 0.5) / g * clip.w;
    }
    gl_Position = clip;
    // yaw-only rotation for normals; boxes are axis-aligned otherwise
    vec3 n = vec3(rot_rep.x * nrm.x + rot_rep.y * nrm.z, nrm.y, -rot_rep.y * nrm.x + rot_rep.x * nrm.z);
    vec3 l = normalize(vec3(0.5, 1.0, 0.3));
    float d = max(dot(normalize(n), l), 0.0);
    shade = color.rgb * mix(vec3(0.42, 0.44, 0.55), vec3(1.05), d);
    float w = mix(1.0, clip.w, res_flags.w);
    uv_w = texcoord0 * rot_rep.z * w;
    w_out = w;
    float z = -(mv * vec4(pos, 1.0)).z;
    fog_f = clamp((z - fog.x) / (fog.y - fog.x), 0.0, 1.0);
}
@end

@fs fs
layout(binding=1) uniform fs_params {
    vec4 fog_color;  // rgb, a = dither on/off
};
layout(binding=0) uniform texture2D tex;
layout(binding=0) uniform sampler smp;
in vec2 uv_w;
in float w_out;
in vec3 shade;
in float fog_f;
out vec4 frag_color;

float b2(vec2 a) { a = floor(a); return fract(a.x * 0.5 + a.y * a.y * 0.75); }
float b4(vec2 a) { return b2(0.5 * a) * 0.25 + b2(a); }

void main() {
    vec2 uv = uv_w / w_out;
    vec3 c = shade * texture(sampler2D(tex, smp), uv).rgb;
    c = mix(c, fog_color.rgb, fog_f);
    if (fog_color.a > 0.5) {
        c = floor(c * 15.0 + b4(gl_FragCoord.xy)) / 15.0;
    }
    frag_color = vec4(c, 1.0);
}
@end

@program ps1 vs fs

// Upscales the low-resolution frame to the window with nearest filtering.
@vs blit_vs
layout(binding=0) uniform blit_params {
    vec4 flip;   // x = 1 to flip v
};
in vec2 bpos;
out vec2 buv;
void main() {
    gl_Position = vec4(bpos * 2.0 - 1.0, 0.0, 1.0);
    buv = vec2(bpos.x, flip.x > 0.5 ? 1.0 - bpos.y : bpos.y);
}
@end

@fs blit_fs
layout(binding=0) uniform texture2D btex;
layout(binding=0) uniform sampler bsmp;
in vec2 buv;
out vec4 frag_color;
void main() { frag_color = vec4(texture(sampler2D(btex, bsmp), buv).rgb, 1.0); }
@end

@program blit blit_vs blit_fs
