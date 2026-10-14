using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// Naughty Dog–style developer menu: narrow vertical "book" list.
/// Root = categories; click drills into a section; Back returns to the list.
/// </summary>
internal sealed class DevMenuPopup : IPanel
{
    public string Name => "Developer Menu";

    const float ListWidth = 230f;
    const float SectionWidth = 300f;
    const float CrashListWidth = 210f;
    const float CrashSectionWidth = 320f;
    /// <summary>See-through card: the menu often stays open while playing.</summary>
    const float CrashOpacity = 0.72f;

    bool _open;
    bool _focusOnce;
    bool _inSection;
    bool _bodyScrolls;
    string _sectionId = "";

    public bool IsOpen
    {
        get => _open;
        set
        {
            if (_open == value) return;
            _open = value;
            _focusOnce = false;
            if (_open)
                _inSection = false; // F3 → category list
        }
    }

    /// <summary>Open drilled into a specific section (e.g. menu bar → Cheats).</summary>
    public void OpenTo(string sectionId)
    {
        if (!string.IsNullOrWhiteSpace(sectionId))
        {
            _sectionId = sectionId;
            ConfigManager.View.DevMenuSection = sectionId;
            _inSection = true;
        }
        _open = true;
        _focusOnce = false;
    }

    /// <summary>
    /// Esc while open: leave a section, or close the menu at the category list.
    /// Called from the host Esc pipeline (ImGui may not see Esc when embedded).
    /// </summary>
    public void HandleEscape()
    {
        if (!_open) return;
        if (_inSection)
            _inSection = false;
        else
            IsOpen = false;
    }

    public void Draw()
    {
        var vp = ImGui.GetMainViewport();
        float ui = Math.Clamp(ImGui.GetIO().FontGlobalScale, 1f, 2.5f);
        bool crash = CrashUi.Enabled;
        float width = crash
            ? (_inSection ? CrashSectionWidth : CrashListWidth) * ui
            : (_inSection ? SectionWidth : ListWidth) * ui;

        // Fixed width; height grows with content (no empty ##selectables — those broke hit-testing).
        ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(width, 0f),
            new Vector2(width, vp.WorkSize.Y * 0.9f));
        // Crash card has no title bar to drag: pin it to the top-left like the Android menu.
        if (crash)
            ImGui.SetNextWindowPos(vp.WorkPos + new Vector2(12 * ui, 12 * ui), ImGuiCond.Always);
        else
            ImGui.SetNextWindowPos(vp.WorkPos + new Vector2(12 * ui, 40 * ui), ImGuiCond.FirstUseEver);

        if (!_focusOnce)
        {
            ImGui.SetNextWindowFocus();
            _focusOnce = true;
        }

        using var chrome = CrashUi.Card(ui, new Vector2(10 * ui, 8 * ui), 0.82f, CrashOpacity);

        // NoScrollbar: auto-fit at fractional UI scales left the content a fraction of a
        // pixel taller than the window, so an always-full scrollbar showed. Long sections
        // scroll inside their own body child instead.
        bool open = _open;
        var flags = ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoDocking
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.AlwaysAutoResize;
        if (crash) flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove;

        if (!ImGui.Begin(Name, ref open, flags))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        if (_inSection && ImGui.IsKeyPressed(ImGuiKey.Backspace))
            _inSection = false;

        CrashUi.CardDecor(ui);
        var sections = DevMenuRegistry.Sections;
        bool close = !_inSection
            ? DrawRootList(sections, ui)
            : DrawSectionPage(sections, ui);
        if (close) open = false;

        IsOpen = open;
        ImGui.End();
    }

    /// <returns>True when the header close button was clicked.</returns>
    bool DrawRootList(IReadOnlyList<IDevMenuSection> sections, float ui)
    {
        bool crash = CrashUi.Enabled;
        bool close = crash
            ? CrashUi.Header($"Crash Recompiled v{AppVersion.Version}", "Dev Menu", ui, closeButton: true, titlePx: 22f)
            : CrashUi.Header("", "Developer", ui);

        if (sections.Count == 0)
        {
            ImGuiEx.TextDisabled("No sections.");
            return close;
        }

        foreach (var s in sections)
        {
            if (CrashUi.Row($"{s.Title}...", s.Id, ui, px: 15f))
            {
                _sectionId = s.Id;
                ConfigManager.View.DevMenuSection = s.Id;
                ConfigManager.SaveView(PanelManager.Panels);
                _inSection = true;
            }
        }

        CrashUi.Divider(ui);
        CrashUi.Hints(ui, (ConfigManager.View.CheatMenuKey, "Close"), ("F1", "Top bar"));
        if (!crash) // Crash style shows the version in the header
            ImGuiEx.TextDisabled($"Version {AppVersion.Version}");
        return close;
    }

    /// <returns>True when the header close button was clicked.</returns>
    bool DrawSectionPage(IReadOnlyList<IDevMenuSection> sections, float ui)
    {
        var section = FindSection(sections, _sectionId);
        bool close = false;
        if (CrashUi.Enabled)
            close = CrashUi.Header("Dev Menu", section?.Title ?? "Dev Menu", ui, closeButton: true, titlePx: 22f);

        if (CrashUi.Row("< Back", "dev-back", ui, px: 13f))
        {
            _inSection = false;
            return close;
        }

        if (!CrashUi.Enabled)
        {
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (section == null)
        {
            ImGuiEx.TextDisabled("Section not found.");
            return close;
        }

        if (CrashUi.Enabled)
            CrashUi.Divider(ui);
        else
        {
            ImGui.TextUnformatted(section.Title.ToUpperInvariant());
            ImGui.Spacing();
        }

        // Body shrinks to short sections and scrolls past the cap. The scrollbar is only
        // shown when last frame really overflowed (fit rounding alone must not show one).
        float maxBody = ImGui.GetMainViewport().WorkSize.Y * 0.6f;
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, maxBody));
        ImGui.BeginChild("##dev-section-body", Vector2.Zero, ImGuiChildFlags.AutoResizeY,
            _bodyScrolls ? ImGuiWindowFlags.None : ImGuiWindowFlags.NoScrollbar);
        section.Draw();
        _bodyScrolls = ImGui.GetScrollMaxY() > 1f;
        ImGui.EndChild();
        return close;
    }

    static IDevMenuSection? FindSection(IReadOnlyList<IDevMenuSection> sections, string id)
    {
        foreach (var s in sections)
            if (s.Id == id) return s;
        return null;
    }
}
