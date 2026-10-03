using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Bonk.Core.Actions;
using Bonk.Core.Models;

namespace Bonk.App.Actions;

public static class ActionExecutor
{
    public static void Execute(ActionSpec action)
    {
        if (action.Validate() is { } error) throw new ArgumentException(error);
        switch (action.Type)
        {
            case ActionType.Keyboard:
                KeyboardShortcut.TryParse(action.Shortcut, out var keys, out _);
                SendKeys(keys);
                break;
            case ActionType.Media:
                SendKeys([action.Value switch
                {
                    "NextTrack" => 0xB0, "PreviousTrack" => 0xB1, "Stop" => 0xB2, "PlayPause" => 0xB3,
                    "VolumeMute" => 0xAD, "VolumeDown" => 0xAE, _ => 0xAF
                }]);
                break;
            case ActionType.Mouse:
                var (down, up, data) = action.Value switch
                {
                    "Left" => (0x0002u, 0x0004u, 0u), "Right" => (0x0008u, 0x0010u, 0u),
                    "Middle" => (0x0020u, 0x0040u, 0u), "Back" => (0x0080u, 0x0100u, 1u),
                    _ => (0x0080u, 0x0100u, 2u)
                };
                Send([MouseInput(down, data), MouseInput(up, data)]);
                break;
            case ActionType.LaunchApplication:
                Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(action.Value))
                    { UseShellExecute = true, Arguments = action.Arguments })?.Dispose();
                break;
            case ActionType.Command:
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("-NoLogo");
                info.ArgumentList.Add("-NoProfile");
                info.ArgumentList.Add("-Command");
                info.ArgumentList.Add(action.Value);
                Process.Start(info)?.Dispose();
                break;
        }
    }

    private static void SendKeys(ushort[] keys)
    {
        // Respect physically held modifiers; do not release the user's keys after our chord.
        var pressed = keys.Where(key => (GetAsyncKeyState(key) & 0x8000) == 0).ToArray();
        if (keys.Any(key => !KeyboardShortcut.IsModifier(key) && (GetAsyncKeyState(key) & 0x8000) != 0))
            throw new InvalidOperationException("The shortcut key is already held down. Release it and try again.");
        var inputs = pressed.Select(key => KeyboardInput(key, false))
            .Concat(pressed.Reverse().Select(key => KeyboardInput(key, true))).ToArray();
        Send(inputs);
    }

    private static Input KeyboardInput(ushort key, bool release) => new()
    {
        Type = 1,
        Data = new() { Keyboard = new() { VirtualKey = key, Flags = (release ? 2u : 0u) | (IsExtended(key) ? 1u : 0u) } }
    };
    private static bool IsExtended(ushort key) => key is >= 0x21 and <= 0x2E or 0x5B or 0x6F or 0x90 or >= 0xAD and <= 0xB3;
    private static Input MouseInput(uint flags, uint data) => new() { Data = new() { Mouse = new() { Flags = flags, MouseData = data } } };

    private static void Send(Input[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return;
        var error = Marshal.GetLastWin32Error();
        // Best-effort releases if Windows accepted only part of a chord/click.
        var releases = inputs.Where(i => i.Type == 1 ? (i.Data.Keyboard.Flags & 2) != 0
            : (i.Data.Mouse.Flags & (0x0004 | 0x0010 | 0x0040 | 0x0100)) != 0).ToArray();
        if (releases.Length > 0) SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        throw new Win32Exception(error,
            "Windows blocked simulated input. If the target app runs as administrator, restart Bonk as administrator too.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInputData Keyboard;
        [FieldOffset(0)] public MouseInputData Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}
