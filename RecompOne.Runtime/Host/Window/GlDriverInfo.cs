using RecompOne.Runtime.Hle;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Host;

/// <summary>
/// What the game window's desktop GL or GLES driver offers the renderer (#167, #80):
/// how VRAM copies and texture barriers run, and on GLES the framebuffer fetch
/// path and shader version. Selected like Android's AndroidGlesInfo.
/// </summary>
sealed class GlDriverInfo
{
    public required bool Es { get; init; }
    public required int Major { get; init; }
    public required int Minor { get; init; }
    public required string Renderer { get; init; }
    public required string Version { get; init; }
    public required bool CopyImage { get; init; }
    public required bool CoreTextureBarrier { get; init; }
    public string? TextureBarrierFunction { get; init; }
    public nint TextureBarrierAddress { get; init; }
    public GlesFramebufferFetchPath FramebufferFetch { get; init; }
    public bool DualSourceBlend { get; init; }

    public int GlslEsVersion => Major > 3 || Minor >= 2 ? 320 : Minor == 1 ? 310 : 300;

    /// <summary>GLES without framebuffer fetch or dual-source blending cannot build the GPU renderer.</summary>
    public bool HleBlendSupported => !Es || FramebufferFetch != GlesFramebufferFetchPath.None || DualSourceBlend;

    public static GlDriverInfo Capture(GL gl, bool es, Func<string, nint> getProcAddress)
    {
        int major = gl.GetInteger(GLEnum.MajorVersion);
        int minor = gl.GetInteger(GLEnum.MinorVersion);
        bool AtLeast(int m, int n) => major > m || (major == m && minor >= n);

        // Core profiles have no glGetString(GL_EXTENSIONS): read them one by one.
        var extensions = new HashSet<string>(StringComparer.Ordinal);
        int count = gl.GetInteger(GLEnum.NumExtensions);
        for (uint i = 0; i < count; i++)
            if (gl.GetStringS(StringName.Extensions, i) is { Length: > 0 } name)
                extensions.Add(name);

        bool coreBarrier = !es && (AtLeast(4, 5) || extensions.Contains("GL_ARB_texture_barrier"));
        string? barrierFunction = coreBarrier ? null
            : es && extensions.Contains("GL_EXT_texture_barrier") ? "glTextureBarrierEXT"
            : extensions.Contains("GL_NV_texture_barrier") ? "glTextureBarrierNV"
            : null;
        nint barrierAddress = barrierFunction == null ? 0 : TryGetProcAddress(getProcAddress, barrierFunction);

        var fetch = !es ? GlesFramebufferFetchPath.None
            : extensions.Contains("GL_EXT_shader_framebuffer_fetch") ? GlesFramebufferFetchPath.Ext
            : extensions.Contains("GL_ARM_shader_framebuffer_fetch") ? GlesFramebufferFetchPath.Arm
            : GlesFramebufferFetchPath.None;

        return new GlDriverInfo
        {
            Es = es,
            Major = major,
            Minor = minor,
            Renderer = $"{gl.GetStringS(StringName.Renderer)} ({gl.GetStringS(StringName.Vendor)})",
            Version = gl.GetStringS(StringName.Version) ?? $"{major}.{minor}",
            CopyImage = es ? AtLeast(3, 2) : AtLeast(4, 3) || extensions.Contains("GL_ARB_copy_image"),
            CoreTextureBarrier = coreBarrier,
            TextureBarrierFunction = barrierAddress == 0 ? null : barrierFunction,
            TextureBarrierAddress = barrierAddress,
            FramebufferFetch = fetch,
            DualSourceBlend = !es || extensions.Contains("GL_EXT_blend_func_extended"),
        };
    }

    static nint TryGetProcAddress(Func<string, nint> getProcAddress, string name)
    {
        try { return getProcAddress(name); }
        catch { return 0; }
    }

    /// <summary>Apply to the backend before <see cref="GlBackend.InitGl"/>.</summary>
    public void Configure(GlBackend backend)
    {
        backend.CopyImageSupported = CopyImage;
        backend.CoreTextureBarrier = CoreTextureBarrier;
        backend.ConfigureTextureBarrier(TextureBarrierAddress);
    }

    public string Summary =>
        $"{Version} | {Renderer} | copies: {(CopyImage ? "CopyImageSubData" : "blit")}" +
        $" | barrier: {(CoreTextureBarrier ? "glTextureBarrier" : TextureBarrierFunction ?? "flush")}" +
        (Es
            ? $" | GLSL ES {GlslEsVersion} | blend: {FramebufferFetch switch
            {
                GlesFramebufferFetchPath.Ext => "EXT framebuffer fetch",
                GlesFramebufferFetchPath.Arm => "ARM framebuffer fetch",
                _ => DualSourceBlend ? "dual-source" : "none",
            }}"
            : "");
}
