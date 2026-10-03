namespace Bonk.Core.Models;

public enum TapGesture { SingleTap = 1, DoubleTap = 2, TripleTap = 3 }
public enum ActionType { Keyboard, Media, Mouse, LaunchApplication, Command }

public sealed class Binding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TapGesture Trigger { get; set; } = TapGesture.DoubleTap;
    public bool Enabled { get; set; } = true;
    public ActionSpec Action { get; set; } = new();
    public string TriggerLabel => Trigger switch
    {
        TapGesture.SingleTap => "Single tap",
        TapGesture.DoubleTap => "Double tap",
        _ => "Triple tap"
    };
    public string ActionLabel => Action.DisplayName;
}

public sealed class ActionSpec
{
    public ActionType Type { get; set; } = ActionType.Keyboard;
    public string Shortcut { get; set; } = "Ctrl+Shift+M";
    public string Value { get; set; } = "";
    public string Arguments { get; set; } = "";
    public static readonly string[] MediaKeys = ["PlayPause", "NextTrack", "PreviousTrack", "Stop", "VolumeUp", "VolumeDown", "VolumeMute"];
    public static readonly string[] MouseButtons = ["Left", "Right", "Middle", "Back", "Forward"];

    public string DisplayName => Type switch
    {
        ActionType.Keyboard => Shortcut,
        ActionType.Media => Value == "PlayPause" ? "Media play / pause" : $"Media · {Value}",
        ActionType.Mouse => $"{Value} mouse click",
        ActionType.LaunchApplication => $"Launch · {Path.GetFileName(Value)}",
        ActionType.Command => $"Command · {Value}",
        _ => "Unknown action"
    };

    public string? Validate() => Type switch
    {
        ActionType.Keyboard => Actions.KeyboardShortcut.TryParse(Shortcut, out _, out var error) ? null : error,
        ActionType.Media => MediaKeys.Contains(Value) ? null : "Choose a media key.",
        ActionType.Mouse => MouseButtons.Contains(Value) ? null : "Choose a mouse button.",
        ActionType.LaunchApplication or ActionType.Command => string.IsNullOrWhiteSpace(Value) ? "Enter an application path or command." : null,
        _ => "Unknown action type."
    };
}
