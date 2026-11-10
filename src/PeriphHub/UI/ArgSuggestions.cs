using PeriphHub.Core;

namespace PeriphHub.UI;

/// <summary>Ready-made values for action details (so you don't have to remember names).</summary>
public static class ArgSuggestions
{
    public static List<string> For(string actionId, IReadOnlyList<string>? haEntities = null)
    {
        try
        {
            return actionId switch
            {
                "laptop:mode" => new() { "Silent", "Performance", "Turbo" },
                "laptop:kbeffect" => new() { "Static", "Breathing", "Colour cycle", "Strobing" },
                "laptop:kbbright" => new() { "Off", "Low", "Medium", "High" },
                "laptop:kbcolor" => new() { "#FF0000", "#00FF00", "#0080FF", "#FFFFFF", "#FF00FF", "#FFA000", "#00FFFF" },
                "laptop:chargelimit" => new() { "60", "80", "100" },
                "mouse:dpi" => new() { "400", "800", "1000", "1600", "2400", "3200", "4000" },
                "sound:volume" => new() { "10", "25", "50", "75", "100" },
                "sound:micmute" => new() { "on", "off" },
                "sound:output" or "deck:output" => AudioEndpoints.All().Where(e => !e.IsCapture).Select(e => e.Name).ToList(),
                "sound:input" => AudioEndpoints.All().Where(e => e.IsCapture).Select(e => e.Name).ToList(),
                "scene:run" => Core.Scenes.SceneStore.Scenes.Select(s => s.Name).ToList(),
                "deck:page" => Core.Deck.DeckStore.Config.Pages.Where(p => !p.IsFolder).Select(p => p.Name).ToList(),
                "ha:toggle" or "ha:on" or "ha:off" => haEntities?.ToList() ?? new(),
                "ha:service" => new() { "light.turn_on light.example {\"brightness\":128}", "scene.turn_on scene.example", "script.turn_on script.example" },
                "sharex:custom" => Core.Integrations.ShareX.Tasks.Select(t => t.task).ToList(),
                _ => new(),
            };
        }
        catch { return new(); }
    }
}
