using System.Numerics;
using ImGuiNET;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// Settings page drawn inside the pause card (<see cref="PauseMenuPopup"/>): section list on
/// the left, the selected section on the right. Not a window of its own.
/// </summary>
internal sealed class SettingsView
{
    const float SidebarWidth = 150f;
    string _selectedId = "";

    /// <summary>Draws the sidebar and content side by side, <paramref name="height"/> px tall.</summary>
    public void Draw(float ui, float height)
    {
        var sections = SettingsRegistry.Sections;
        var current = ResolveSelection(sections);

        DrawSidebar(sections, current, ui, height);
        ImGui.SameLine();
        DrawContent(current, height);
    }

    ISettingsSection? ResolveSelection(IReadOnlyList<ISettingsSection> sections)
    {
        if (sections.Count == 0) return null;
        foreach (var s in sections)
            if (s.Id == _selectedId) return s;
        _selectedId = sections[0].Id;
        return sections[0];
    }

    void DrawSidebar(IReadOnlyList<ISettingsSection> sections, ISettingsSection? current, float ui, float height)
    {
        ImGui.BeginChild("##settings-sidebar", new Vector2(SidebarWidth * ui, height), ImGuiChildFlags.Border);

        foreach (var s in sections)
        {
            if (ImGui.Selectable($"{s.Title}##sec-{s.Id}", current == s))
                _selectedId = s.Id;
        }

        ImGui.EndChild();
    }

    static void DrawContent(ISettingsSection? current, float height)
    {
        ImGui.BeginChild("##settings-content", new Vector2(0f, height), ImGuiChildFlags.Border);

        if (current == null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            ImGui.TextUnformatted("No settings available.");
            ImGui.PopStyleColor();
        }
        else
        {
            if (CrashUi.Enabled)
                CrashUi.Caption(current.Title);
            else
                ImGui.TextUnformatted(current.Title);
            ImGui.Separator();
            ImGui.Spacing();
            current.Draw();
            foreach (var ext in SettingsRegistry.GetExtensions(current.Id))
                ext();
        }

        ImGui.EndChild();
    }
}
