using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// Optional top bar (F1, off by default): a single Developer Menu button. Settings live
/// in the pause menu; debug panels, mods and mod menus in the Developer Menu.
/// </summary>
internal static class MainMenuBar
{
    public static void Draw()
    {
        bool crash = CrashUi.Enabled;
        bool font = false;
        if (crash)
        {
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, CrashUi.Night with { W = 0.92f });
            ImGui.PushStyleColor(ImGuiCol.Text, CrashUi.Sand);
            ImGui.PushStyleColor(ImGuiCol.Header, CrashUi.Wumpa with { W = 0.35f });
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, CrashUi.Wumpa with { W = 0.45f });
            font = CrashUi.PushBodyBold();
        }

        // EndMainMenuBar only after a successful Begin (ImGui asserts otherwise).
        if (ImGui.BeginMainMenuBar())
        {
            var dev = PanelManager.Get<DevMenuPopup>();
            string label = $"Dev Menu ({ConfigManager.View.CheatMenuKey})";
            if (ImGui.MenuItem(label, null, dev?.IsOpen == true) && dev != null)
                dev.IsOpen = !dev.IsOpen;
            ImGui.EndMainMenuBar();
        }

        if (font) ImGui.PopFont();
        if (crash) ImGui.PopStyleColor(4);
    }
}
