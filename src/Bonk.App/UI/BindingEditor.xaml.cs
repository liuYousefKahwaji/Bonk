using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bonk.Core.Models;

namespace Bonk.App.UI;

public partial class BindingEditor : Window
{
    private readonly Guid _id;
    private readonly bool _enabled;
    private readonly HashSet<TapGesture> _unavailable;
    private bool _ready;
    public Binding? Result { get; private set; }
    public bool Removed { get; private set; }
    private sealed record Choice<T>(T Value, string Label);

    public BindingEditor(Binding? binding, IEnumerable<TapGesture> unavailable)
    {
        InitializeComponent();
        _id = binding?.Id ?? Guid.NewGuid();
        _enabled = binding?.Enabled ?? true;
        _unavailable = unavailable.ToHashSet();
        TriggerBox.ItemsSource = new[] { new Choice<TapGesture>(TapGesture.SingleTap, "Single tap"), new(TapGesture.DoubleTap, "Double tap"), new(TapGesture.TripleTap, "Triple tap") };
        TriggerBox.SelectedValue = binding?.Trigger ?? Enum.GetValues<TapGesture>().FirstOrDefault(t => !_unavailable.Contains(t));
        ActionBox.ItemsSource = new[] { new Choice<ActionType>(ActionType.Keyboard, "Keyboard shortcut"), new(ActionType.Media, "Media key"), new(ActionType.Mouse, "Mouse button"), new(ActionType.LaunchApplication, "Launch application"), new(ActionType.Command, "Run PowerShell command") };
        ActionBox.SelectedValue = binding?.Action.Type ?? ActionType.Keyboard;
        DeleteButton.Visibility = binding is null ? Visibility.Collapsed : Visibility.Visible;
        _ready = true;
        UpdateActionFields();
        if (binding is not null)
        {
            ValueBox.Text = binding.Action.Type == ActionType.Keyboard ? binding.Action.Shortcut : binding.Action.Value;
            ChoiceBox.SelectedItem = binding.Action.Value;
            ArgumentsBox.Text = binding.Action.Arguments;
        }
        else ValueBox.Text = "Ctrl+Shift+M";
    }

    private void ActionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateActionFields(); }

    private void UpdateActionFields()
    {
        var type = (ActionType)ActionBox.SelectedValue;
        var isChoice = type is ActionType.Media or ActionType.Mouse;
        ValueBox.Visibility = isChoice ? Visibility.Collapsed : Visibility.Visible;
        ChoiceBox.Visibility = isChoice ? Visibility.Visible : Visibility.Collapsed;
        ArgumentsPanel.Visibility = type == ActionType.LaunchApplication ? Visibility.Visible : Visibility.Collapsed;
        ChoiceBox.ItemsSource = type == ActionType.Media ? ActionSpec.MediaKeys : ActionSpec.MouseButtons;
        ChoiceBox.SelectedIndex = 0;
        ValueLabel.Text = type switch { ActionType.Keyboard => "Shortcut", ActionType.Media => "Media key", ActionType.Mouse => "Mouse button", ActionType.LaunchApplication => "Application path", _ => "PowerShell command" };
        HelpText.Text = type switch
        {
            ActionType.Keyboard => "Press a shortcut here to record it, or type one (Ctrl+Shift+M). Shortcuts go to the active app unless the target has a global keybind.",
            ActionType.Media => "Media keys use the Windows media controls.",
            ActionType.Mouse => "Clicks happen at the current pointer position.",
            ActionType.LaunchApplication => "Enter an executable path. Environment variables such as %LocalAppData% are supported.",
            _ => "Runs the command locally in Windows PowerShell without loading your profile. Only use commands you intend to execute."
        };
        ErrorText.Text = "";
    }

    private void RecordShortcut(object sender, KeyEventArgs e)
    {
        if (!_ready || (ActionType)ActionBox.SelectedValue != ActionType.Keyboard) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        // Plain typing and editing remain available; modified chords and function keys record.
        if (modifiers == ModifierKeys.None && (key < Key.F1 || key > Key.F24)) return;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var token = key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key switch
        {
            Key.OemSemicolon => "Semicolon", Key.OemPlus => "Equals", Key.OemComma => "Comma", Key.OemMinus => "Minus",
            Key.OemPeriod => "Period", Key.OemQuestion => "Slash", Key.OemTilde => "Backtick", Key.OemOpenBrackets => "LeftBracket",
            Key.OemPipe => "Backslash", Key.OemCloseBrackets => "RightBracket", Key.OemQuotes => "Quote",
            Key.Back => "Backspace", Key.Capital => "CapsLock", Key.Snapshot => "PrintScreen", _ => key.ToString()
        };
        parts.Add(token);
        ValueBox.Text = string.Join('+', parts);
        ValueBox.CaretIndex = ValueBox.Text.Length;
        e.Handled = true;
    }

    private void SaveBinding(object sender, RoutedEventArgs e)
    {
        if (TriggerBox.SelectedValue is not TapGesture trigger || _unavailable.Contains(trigger))
        { ErrorText.Text = "That gesture already has a binding. Edit its existing binding, or choose another gesture."; return; }
        var type = (ActionType)ActionBox.SelectedValue;
        var action = new ActionSpec { Type = type, Shortcut = ValueBox.Text.Trim(), Value = type is ActionType.Media or ActionType.Mouse ? ChoiceBox.SelectedItem as string ?? "" : ValueBox.Text.Trim(), Arguments = ArgumentsBox.Text };
        if (action.Validate() is { } error) { ErrorText.Text = error; return; }
        Result = new() { Id = _id, Trigger = trigger, Enabled = _enabled, Action = action };
        DialogResult = true;
    }

    private void DeleteBinding(object sender, RoutedEventArgs e) { Removed = true; DialogResult = true; }
}
