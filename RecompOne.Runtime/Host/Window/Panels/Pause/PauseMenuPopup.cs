using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Cheats;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// ESC pause overlay: resume, settings, open the in-game save/exit flow, or leave the session.
/// Drawn in the launcher look (<see cref="CrashUi"/>) unless CrashMenuStyle is off. Settings
/// is a second page of the same card (<see cref="SettingsView"/>), not a separate window.
/// </summary>
internal sealed class PauseMenuPopup : IPanel
{
    public string Name => "Pause";

    const float MenuWidth = 260f;
    const float CrashWidth = 320f;
    const float SettingsWidth = 760f;
    const float SettingsHeight = 520f;

    bool _open;
    bool _focusOnce;
    bool _settingsPage;
    float _footerHeight;
    readonly SettingsView _settings = new();

    public bool IsOpen
    {
        get => _open;
        set
        {
            if (_open == value) return;
            _open = value;
            _focusOnce = false;
            _settingsPage = false;
        }
    }

    /// <summary>Esc: Settings page back to the pause page, otherwise toggle the overlay.</summary>
    public void HandleEscape()
    {
        if (_open && _settingsPage)
            _settingsPage = false;
        else
            IsOpen = !_open;
    }

    public void Draw()
    {
        var vp = ImGui.GetMainViewport();
        float ui = Math.Clamp(ImGui.GetIO().FontGlobalScale, 1f, 2.5f);
        bool crash = CrashUi.Enabled;
        bool settingsPage = _settingsPage;

        CrashUi.Backdrop("##pause-dim", vp, ui);

        if (settingsPage)
        {
            ImGui.SetNextWindowSize(new Vector2(
                Math.Min(SettingsWidth * ui, vp.WorkSize.X * 0.95f),
                Math.Min(SettingsHeight * ui, vp.WorkSize.Y * 0.9f)), ImGuiCond.Always);
        }
        else
        {
            float width = (crash ? CrashWidth : MenuWidth) * ui;
            ImGui.SetNextWindowSize(new Vector2(width, 0f), ImGuiCond.Always);
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(width, 0f),
                new Vector2(width, vp.WorkSize.Y * 0.9f));
        }
        ImGui.SetNextWindowPos(vp.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));

        if (!_focusOnce)
        {
            ImGui.SetNextWindowFocus();
            _focusOnce = true;
        }

        using var chrome = CrashUi.Card(ui, new Vector2(12 * ui, 10 * ui), 0.92f);

        // Local copy: ImGui may clear this via the title-bar X. Menu actions must
        // mutate `open` too — assigning only IsOpen was overwritten by `IsOpen = open`.
        bool open = _open;
        var flags = ImGuiWindowFlags.NoCollapse
                    | ImGuiWindowFlags.NoDocking
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoResize
                    | ImGuiWindowFlags.NoMove
                    // Auto-fit at fractional UI scales showed an always-full scrollbar.
                    | ImGuiWindowFlags.NoScrollbar;
        if (!settingsPage) flags |= ImGuiWindowFlags.AlwaysAutoResize;
        if (crash) flags |= ImGuiWindowFlags.NoTitleBar;

        if (!ImGui.Begin(Name, ref open, flags))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }

        CrashUi.CardDecor(ui);
        if (settingsPage)
        {
            DrawSettingsPage(ui, crash);
            IsOpen = open;
            ImGui.End();
            return;
        }

        CrashUi.Header("Crash Recompiled", "Paused", ui);

        if (CrashUi.Row("Resume", "resume", ui))
            open = false;

        bool saveStates = Config.ConfigManager.View.SaveStates;
        if (saveStates && DrawSaveStates(ui))
            open = false;

        // Settings replaces the rows inside this card, so gameplay input stays muted.
        if (CrashUi.Row("Settings", "settings", ui))
            _settingsPage = true;

        if (CrashUi.Row("Exit to Map...", "exit-map", ui, enabled: !CheatManager.IsOnTitleMenuMap()))
        {
            // Native Crash 1 path: Start (pause) → Select (return to map).
            ExitToMapInjector.Begin();
            open = false;
        }

        string leaveLabel = HostWindow.IsEmbedded ? "Return to Launcher" : "Quit";
        if (CrashUi.Row(leaveLabel, "leave", ui))
        {
            open = false;
            // Never Close() mid-ImGui frame — defer to Present after DoRender.
            HostWindow.RequestEndSession();
        }

        CrashUi.Divider(ui);

        var game = Config.ConfigManager.Game;
        float pct = game.MasterVolume * 100f;
        if (crash)
        {
            CrashUi.Caption("Volume");
            ImGui.SetNextItemWidth(-1f);
        }
        if (ImGui.SliderFloat(crash ? "##volume" : "Volume", ref pct, 0f, 100f, "%.0f%%"))
        {
            game.MasterVolume = Math.Clamp(pct / 100f, 0f, 1f);
            Audio.SetMasterVolume(game.Muted ? 0f : game.MasterVolume);
            Config.ConfigManager.SaveGame();
        }

        bool muted = game.Muted;
        if (ImGui.Checkbox("Mute", ref muted))
        {
            game.Muted = muted;
            Audio.SetMasterVolume(muted ? 0f : game.MasterVolume);
            Config.ConfigManager.SaveGame();
        }

        CrashUi.Divider(ui);
        if (saveStates)
            CrashUi.Hints(ui, ("Esc", "Resume"), ("F5", "Save"), ("F8", "Load"), ("F6/F7", "Slot"));
        else
            CrashUi.Hints(ui, ("Esc", "Resume"));

        IsOpen = open;
        ImGui.End();
    }

    /// <summary>Settings page: header with back button, sections, key hints.</summary>
    void DrawSettingsPage(float ui, bool crash)
    {
        if (CrashUi.Header("Paused", "Settings", ui, closeButton: true))
            _settingsPage = false;
        if (!crash && ImGui.Button("< Back"))
            _settingsPage = false;

        // Footer height from the last frame keeps the hints pinned to the card bottom.
        float spacing = ImGui.GetStyle().ItemSpacing.Y;
        float footer = _footerHeight > 0f ? _footerHeight : 34f * ui;
        float body = Math.Max(80f * ui, ImGui.GetContentRegionAvail().Y - footer - spacing);
        _settings.Draw(ui, body);

        float start = ImGui.GetCursorPosY();
        CrashUi.Divider(ui);
        CrashUi.Hints(ui, ("Esc", "Back"));
        _footerHeight = ImGui.GetCursorPosY() - start;
    }

    /// <summary>Quick save rows (#130). True when an action should close the menu.</summary>
    static bool DrawSaveStates(float ui)
    {
        int slot = SaveStates.Slot;
        var info = SaveStates.GetInfo(slot);
        bool close = false;
        string tag = $"Slot {slot}";

        if (CrashUi.Row("Save State", "state-save", ui, tag: tag))
        {
            SaveStates.RequestSave(slot);
            close = true;
        }

        if (CrashUi.Row("Load State", "state-load", ui, enabled: info is { Compatible: true }, tag: tag))
        {
            SaveStates.RequestLoad(slot);
            close = true;
        }

        // Crash style: line up with the row labels (past the Wumpa cursor gutter, 1.45 × 20 px).
        float indent = CrashUi.Enabled ? 29f * ui : ImGui.GetStyle().IndentSpacing;
        ImGui.Indent(indent);
        if (ImGui.ArrowButton("##state-prev", ImGuiDir.Left)) SaveStates.StepSlot(-1);
        ImGui.SameLine();
        if (ImGui.ArrowButton("##state-next", ImGuiDir.Right)) SaveStates.StepSlot(1);
        ImGui.SameLine();
        ImGui.TextUnformatted($"Slot {slot}");
        ImGui.PushTextWrapPos(0f);
        ImGuiEx.TextDisabled(info?.Describe() ?? "Empty");
        ImGui.PopTextWrapPos();
        ImGui.Unindent(indent);
        return close;
    }
}
