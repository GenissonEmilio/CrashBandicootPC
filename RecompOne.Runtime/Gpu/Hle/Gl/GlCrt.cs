using System.Diagnostics;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

/// <summary>
/// Optional "MattiasCRT" output filter (#131): the TV look from crtemu.h in Mattias
/// Gustavsson's CRT View (https://github.com/mattiasgustavsson/crtview, MIT; notice in
/// THIRD-PARTY-NOTICES.txt). Curvature, colour bleed, ghosting, phosphor persistence,
/// scanlines, shadow mask, filmic tone map, noise and flicker are the original shader.
///
/// Differences from crtemu: the persistence and ghost buffers run at the PS1's native
/// line count, so the glow looks the same at every internal resolution, while the
/// main image stays the full-resolution frame. Persistence decays on a fixed 60 Hz
/// step (crtemu decays once per present, which would smear longer at 144+ Hz).
/// Scanlines follow the source lines and fade out when the output cannot show them.
/// The final pass runs at the exact output size so the shadow mask lands on screen pixels.
/// </summary>
internal sealed class GlCrt
{
    const double StepSeconds = 1.0 / 60.0;
    const int Cur = 0, StateA = 1, StateB = 2, Scratch = 3, Ghost = 4;

    const string DownFs = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSrc;
        uniform vec2 uSpan;
        out vec4 oColor;
        void main() {
            // Four bilinear taps inside one destination texel: enough averaging for
            // a buffer that is only ever blurred further.
            vec2 d = uSpan * 0.25;
            vec3 c = texture(uSrc, vUv + vec2(-d.x, -d.y)).rgb
                   + texture(uSrc, vUv + vec2( d.x, -d.y)).rgb
                   + texture(uSrc, vUv + vec2(-d.x,  d.y)).rgb
                   + texture(uSrc, vUv + vec2( d.x,  d.y)).rgb;
            oColor = vec4(c * 0.25, 1.0);
        }
        """;

    // crtemu's 9-tap Gaussian. Mode 1 folds in the persistence update
    // max(cur, blur * k); mode 2 blurs max(cur, src * k) (the blended image).
    const string BlurFs = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSrc;
        uniform sampler2D uCur;
        uniform vec2 uDir;
        uniform int uMode;
        uniform float uK;
        out vec4 oColor;
        vec3 tap(vec2 uv) {
            vec3 s = texture(uSrc, uv).rgb;
            return uMode == 2 ? max(texture(uCur, uv).rgb, s * uK) : s;
        }
        void main() {
            vec3 sum = tap(vUv) * 0.2270270270;
            sum += (tap(vUv - uDir) + tap(vUv + uDir)) * 0.1945945946;
            sum += (tap(vUv - 2.0 * uDir) + tap(vUv + 2.0 * uDir)) * 0.1216216216;
            sum += (tap(vUv - 3.0 * uDir) + tap(vUv + 3.0 * uDir)) * 0.0540540541;
            sum += (tap(vUv - 4.0 * uDir) + tap(vUv + 4.0 * uDir)) * 0.0162162162;
            if (uMode == 1) sum = max(texture(uCur, vUv).rgb, sum * uK);
            oColor = vec4(sum, 1.0);
        }
        """;

    // vUv follows crtemu: (0,0) is the bottom-left of the picture. Flip when the
    // target is a texture shown top row first (ImGui) instead of a window surface.
    const string CrtVs = """
        #version 330 core
        layout(location = 0) in vec2 aPos;
        uniform float uFlipY;
        out vec2 vUv;
        void main() {
            vUv = aPos * 0.5 + 0.5;
            if (uFlipY > 0.5) vUv.y = 1.0 - vUv.y;
            gl_Position = vec4(aPos, 0.0, 1.0);
        }
        """;

    const string CrtFs = """
        #version 330 core
        // MattiasCRT - adapted from crtemu.h (TV) in CRT View by Mattias Gustavsson.
        // MIT License, Copyright (c) 2016 Mattias Gustavsson.
        in vec2 vUv;
        uniform sampler2D uFrame;
        uniform sampler2D uState;
        uniform sampler2D uGhost;
        uniform vec2 uResolution;
        uniform float uTime;
        uniform float uLines;
        out vec4 oColor;

        vec3 fetch(sampler2D s, vec2 tc) {
            // Black outside the picture, like crtemu's GL_CLAMP_TO_BORDER.
            float edge = 0.502 - max(abs(0.5 - tc.x), abs(0.5 - tc.y));
            return texture(s, vec2(tc.x, 1.0 - tc.y)).rgb * clamp(edge * 400.0, 0.0, 1.0);
        }

        vec3 tsample(vec2 tc) {
            vec3 s = max(fetch(uFrame, tc), fetch(uState, tc) * 0.32);
            return pow(abs(s), vec3(2.2)) * 1.25;
        }

        vec3 gsample(vec2 tc) {
            return pow(abs(fetch(uGhost, tc)), vec3(2.2)) * 1.25;
        }

        vec3 filmic(vec3 linearColor) {
            vec3 x = max(vec3(0.0), linearColor - vec3(0.004));
            return (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
        }

        vec2 curve(vec2 uv) {
            uv = (uv - 0.5) * 2.0;
            uv *= vec2(1.049, 1.042);
            uv -= vec2(-0.008, 0.008);
            uv.x *= 1.0 + pow(abs(uv.y) / 5.0, 2.0);
            uv.y *= 1.0 + pow(abs(uv.x) / 4.0, 2.0);
            return uv / 2.0 + 0.5;
        }

        float rand(vec2 co) {
            return fract(sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453);
        }

        void main() {
            /* Curve */
            vec2 curved_uv = mix(curve(vUv), vUv, 0.4);
            vec2 scuv = curved_uv;

            /* Main color, Bleed */
            vec3 col;
            float x = sin(0.1 * uTime + curved_uv.y * 13.0) * sin(0.23 * uTime + curved_uv.y * 19.0)
                    * sin(0.3 + 0.11 * uTime + curved_uv.y * 23.0) * 0.0012;
            float o = sin(gl_FragCoord.y / 1.5) / uResolution.x;
            x += o * 0.25;
            x *= 0.2;
            col.r = tsample(vec2(x + scuv.x + 0.0009, scuv.y + 0.0009)).x + 0.02;
            col.g = tsample(vec2(x + scuv.x + 0.0000, scuv.y - 0.0011)).y + 0.02;
            col.b = tsample(vec2(x + scuv.x - 0.0015, scuv.y + 0.0000)).z + 0.02;
            float i = clamp(col.r * 0.299 + col.g * 0.587 + col.b * 0.114, 0.0, 1.0);
            i = pow(1.0 - pow(i, 2.0), 1.0);
            i = (1.0 - i) * 0.85 + 0.15;

            /* Ghosting */
            float ghs = 0.15;
            vec3 r = gsample(vec2(x - 0.014, -0.027) * 0.85 + 0.007 * vec2(0.35 * sin(1.0 / 7.0 + 15.0 * curved_uv.y + 0.9 * uTime),
                0.35 * sin(2.0 / 7.0 + 10.0 * curved_uv.y + 1.37 * uTime)) + vec2(scuv.x + 0.001, scuv.y + 0.001)) * vec3(0.5, 0.25, 0.25);
            vec3 g = gsample(vec2(x - 0.019, -0.020) * 0.85 + 0.007 * vec2(0.35 * cos(1.0 / 9.0 + 15.0 * curved_uv.y + 0.5 * uTime),
                0.35 * sin(2.0 / 9.0 + 10.0 * curved_uv.y + 1.50 * uTime)) + vec2(scuv.x + 0.000, scuv.y - 0.002)) * vec3(0.25, 0.5, 0.25);
            vec3 b = gsample(vec2(x - 0.017, -0.003) * 0.85 + 0.007 * vec2(0.35 * sin(2.0 / 3.0 + 15.0 * curved_uv.y + 0.7 * uTime),
                0.35 * cos(2.0 / 3.0 + 10.0 * curved_uv.y + 1.63 * uTime)) + vec2(scuv.x - 0.002, scuv.y + 0.000)) * vec3(0.25, 0.25, 0.5);

            col += vec3(ghs * (1.0 - 0.299)) * pow(clamp(vec3(3.0) * r, vec3(0.0), vec3(1.0)), vec3(2.0)) * vec3(i);
            col += vec3(ghs * (1.0 - 0.587)) * pow(clamp(vec3(3.0) * g, vec3(0.0), vec3(1.0)), vec3(2.0)) * vec3(i);
            col += vec3(ghs * (1.0 - 0.114)) * pow(clamp(vec3(3.0) * b, vec3(0.0), vec3(1.0)), vec3(2.0)) * vec3(i);

            /* Level adjustment (curves) */
            col *= vec3(0.95, 1.05, 0.95);
            col = clamp(col * 1.3 + 0.75 * col * col + 1.25 * col * col * col * col * col, vec3(0.0), vec3(10.0));

            /* Vignette */
            float vig = 0.1 + 16.0 * curved_uv.x * curved_uv.y * (1.0 - curved_uv.x) * (1.0 - curved_uv.y);
            vig = 1.3 * pow(max(vig, 0.0), 0.5);
            col *= vig;

            /* Scanlines: one per source line, fading out below ~2 screen pixels per line */
            float lineAmp = smoothstep(1.5, 3.0, uResolution.y / uLines);
            float scans = clamp(0.35 + 0.18 * lineAmp * sin(6.0 * uTime + curved_uv.y * uLines * 6.2831853), 0.0, 1.0);
            col *= vec3(pow(scans, 0.9));

            /* Vertical lines (shadow mask) */
            col *= 1.0 - 0.23 * clamp(mod(gl_FragCoord.x, 3.0) / 2.0, 0.0, 1.0);

            /* Tone map */
            col = filmic(col);

            /* Noise */
            vec2 seed = curved_uv * uResolution;
            col -= 0.015 * pow(vec3(rand(seed + uTime), rand(seed + uTime * 2.0), rand(seed + uTime * 3.0)), vec3(1.5));

            /* Flicker */
            col *= 1.0 - 0.004 * (sin(50.0 * uTime + curved_uv.y * 2.0) * 0.5 + 0.5);

            /* Clamp */
            if (curved_uv.x < 0.0 || curved_uv.x > 1.0 || curved_uv.y < 0.0 || curved_uv.y > 1.0)
                col = vec3(0.0);
            oColor = vec4(col, 1.0);
        }
        """;

    readonly GL _gl;
    readonly uint _quadVao;
    readonly bool _gles;
    uint _progDown, _progBlur, _progCrt;
    int _uDownSpan, _uBlurDir, _uBlurMode, _uBlurK, _uCrtFlip, _uCrtRes, _uCrtTime, _uCrtLines;
    readonly uint[] _tex = new uint[5];
    readonly uint[] _fbo = new uint[5];
    int _lowW, _lowH, _lines = 240, _state = StateA;
    uint _outTex, _outFbo;
    int _outW, _outH;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    double _lastSeconds, _stepDebt;

    public GlCrt(GL gl, uint quadVao, bool gles) => (_gl, _quadVao, _gles) = (gl, quadVao, gles);

    public bool Init()
    {
        _progDown = GlShaders.Build(_gl, GlShaders.FullscreenVs, DownFs, "crt-down", _gles);
        _progBlur = GlShaders.Build(_gl, GlShaders.FullscreenVs, BlurFs, "crt-blur", _gles);
        _progCrt = GlShaders.Build(_gl, CrtVs, CrtFs, "crt", _gles);
        if (_progDown == 0 || _progBlur == 0 || _progCrt == 0) return false;

        _uDownSpan = _gl.GetUniformLocation(_progDown, "uSpan");
        _gl.UseProgram(_progDown);
        _gl.Uniform1(_gl.GetUniformLocation(_progDown, "uSrc"), 0);

        _uBlurDir = _gl.GetUniformLocation(_progBlur, "uDir");
        _uBlurMode = _gl.GetUniformLocation(_progBlur, "uMode");
        _uBlurK = _gl.GetUniformLocation(_progBlur, "uK");
        _gl.UseProgram(_progBlur);
        _gl.Uniform1(_gl.GetUniformLocation(_progBlur, "uSrc"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_progBlur, "uCur"), 1);

        _uCrtFlip = _gl.GetUniformLocation(_progCrt, "uFlipY");
        _uCrtRes = _gl.GetUniformLocation(_progCrt, "uResolution");
        _uCrtTime = _gl.GetUniformLocation(_progCrt, "uTime");
        _uCrtLines = _gl.GetUniformLocation(_progCrt, "uLines");
        _gl.UseProgram(_progCrt);
        _gl.Uniform1(_gl.GetUniformLocation(_progCrt, "uFrame"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_progCrt, "uState"), 1);
        _gl.Uniform1(_gl.GetUniformLocation(_progCrt, "uGhost"), 2);

        for (int i = 0; i < _tex.Length; i++)
        {
            _tex[i] = _gl.GenTexture();
            _fbo[i] = _gl.GenFramebuffer();
        }
        _outTex = _gl.GenTexture();
        _outFbo = _gl.GenFramebuffer();
        return true;
    }

    /// <summary>Filter <paramref name="frameTex"/> into an owned w×h texture, top row first (desktop/ImGui).</summary>
    public unsafe uint RenderToTexture(uint frameTex, bool frameNearest, int nativeH, float aspect, int width, int height)
    {
        if (width != _outW || height != _outH)
        {
            _gl.BindTexture(TextureTarget.Texture2D, _outTex);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, null);
            // Drawn 1:1 on screen pixels; nearest keeps the shadow mask crisp.
            SetParams(GLEnum.Nearest);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _outFbo);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _outTex, 0);
            _outW = width;
            _outH = height;
        }
        Render(frameTex, frameNearest, nativeH, aspect, _outFbo, 0, 0, width, height, flipY: true);
        return _outTex;
    }

    /// <summary>Filter <paramref name="frameTex"/> into the given framebuffer rectangle.</summary>
    public void Render(uint frameTex, bool frameNearest, int nativeH, float aspect,
        uint targetFbo, int x, int y, int width, int height, bool flipY)
    {
        _lines = Math.Clamp(nativeH, 120, 576);
        int lowH = _lines;
        int lowW = Math.Clamp((int)MathF.Round(lowH * aspect), 64, 2048);
        EnsureLow(lowW, lowH);

        double now = _clock.Elapsed.TotalSeconds;
        _stepDebt = Math.Min(_stepDebt + Math.Max(0.0, now - _lastSeconds), StepSeconds * 4);
        _lastSeconds = now;

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.BindVertexArray(_quadVao);

        // crtemu samples its backbuffer bilinearly; the present texture is nearest
        // at 1x internal or with crisp pixels on.
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, frameTex);
        if (frameNearest) SetFilter(GLEnum.Linear);

        _gl.Viewport(0, 0, (uint)lowW, (uint)lowH);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[Cur]);
        _gl.UseProgram(_progDown);
        _gl.Uniform2(_uDownSpan, 1f / lowW, 1f / lowH);
        DrawQuad();

        _gl.UseProgram(_progBlur);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _tex[Cur]);
        while (_stepDebt >= StepSeconds)
        {
            // Phosphor persistence: state = max(cur, blur(state) * 0.96).
            int next = _state == StateA ? StateB : StateA;
            Blur(_tex[_state], Scratch, 1f / lowW, 0f, 0, 0f);
            Blur(_tex[Scratch], next, 0f, 1f / lowH, 1, 0.96f);
            _state = next;
            _stepDebt -= StepSeconds;
        }
        // Ghost image: blur(max(cur, state * 0.32)).
        Blur(_tex[_state], Scratch, 1f / lowW, 0f, 2, 0.32f);
        Blur(_tex[Scratch], Ghost, 0f, 1f / lowH, 0, 0f);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, targetFbo);
        _gl.Viewport(x, y, (uint)width, (uint)height);
        _gl.UseProgram(_progCrt);
        _gl.Uniform1(_uCrtFlip, flipY ? 1f : 0f);
        _gl.Uniform2(_uCrtRes, (float)width, (float)height);
        // crtemu runs its clock at 1.5x; wrap hourly to keep float precision.
        _gl.Uniform1(_uCrtTime, (float)(1.5 * (now % 3600.0)));
        _gl.Uniform1(_uCrtLines, (float)_lines);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, _tex[Ghost]);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _tex[_state]);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, frameTex);
        DrawQuad();

        if (frameNearest) SetFilter(GLEnum.Nearest);
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    void Blur(uint source, int target, float dx, float dy, int mode, float k)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[target]);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, source);
        _gl.Uniform2(_uBlurDir, dx, dy);
        _gl.Uniform1(_uBlurMode, mode);
        _gl.Uniform1(_uBlurK, k);
        DrawQuad();
    }

    void DrawQuad() => _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

    void EnsureLow(int width, int height)
    {
        if (width == _lowW && height == _lowH) return;
        // Half float keeps the 0.96 decay from sticking at a dim 8-bit floor.
        if (!AllocateLow(width, height, halfFloat: true))
            AllocateLow(width, height, halfFloat: false);
        _lowW = width;
        _lowH = height;
        _state = StateA;
        _stepDebt = 0;
    }

    unsafe bool AllocateLow(int width, int height, bool halfFloat)
    {
        bool complete = true;
        for (int i = 0; i < _tex.Length; i++)
        {
            _gl.BindTexture(TextureTarget.Texture2D, _tex[i]);
            _gl.TexImage2D(TextureTarget.Texture2D, 0, halfFloat ? InternalFormat.Rgba16f : InternalFormat.Rgba8,
                (uint)width, (uint)height, 0, PixelFormat.Rgba,
                halfFloat ? PixelType.Float : PixelType.UnsignedByte, null);
            SetParams(GLEnum.Linear);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo[i]);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _tex[i], 0);
            if (_gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            {
                complete = false;
                break;
            }
            // Fresh persistence starts black, never undefined memory.
            _gl.ClearColor(0f, 0f, 0f, 1f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
        }
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return complete;
    }

    void SetParams(GLEnum filter)
    {
        SetFilter(filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    void SetFilter(GLEnum filter)
    {
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)filter);
    }

    public void Dispose()
    {
        foreach (var t in _tex) if (t != 0) _gl.DeleteTexture(t);
        foreach (var f in _fbo) if (f != 0) _gl.DeleteFramebuffer(f);
        if (_outTex != 0) _gl.DeleteTexture(_outTex);
        if (_outFbo != 0) _gl.DeleteFramebuffer(_outFbo);
        if (_progDown != 0) _gl.DeleteProgram(_progDown);
        if (_progBlur != 0) _gl.DeleteProgram(_progBlur);
        if (_progCrt != 0) _gl.DeleteProgram(_progCrt);
    }
}
