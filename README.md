# Bonk

**Your microphone now has a side job.**

Bonk is a small Windows utility that turns gentle taps on your headset or microphone into useful actions. Two taps to mute Discord, three to deafen, one to pause your music. A very modest percussion instrument.

```text
bonk bonk → Ctrl + Shift + M → Discord mute
```

## What it does

- Detects single, double, and triple taps using an adaptive noise floor, sudden-rise detection, and a short-decay check.
- Runs keyboard shortcuts, media keys, mouse clicks, applications, or PowerShell commands.
- Shows a live meter, recent input graph, trigger threshold, and a bounded event log.
- Lets you tune sensitivity, minimum strength, debounce, tap spacing, and completion timeout.
- Lives in the system tray, with pause/resume, a five-minute pause, and test mode.
- Remembers your microphone and settings, retries disconnected devices, and can start with Windows.

Everything runs locally. **Bonk never saves microphone audio or sends it anywhere.** The event log stays in memory; configuration is a small JSON file. There are no accounts, cloud services, or speech recognition.

## Getting started

You need Windows 10 or 11 and a recording device. Portable builds target Windows x64 and include the .NET runtime.

1. Extract a portable build and run `Bonk.exe`, or [build it from source](#build-from-source).
2. Choose your microphone. **Windows default microphone** follows the default communications recording device.
3. Leave **Test mode** on while you try a few gentle taps. Watch **What Bonk heard**.
4. Edit a binding, choose an action, and save. Disable any gestures you do not want.
5. Turn test mode off when detection looks right. Actions are now live.
6. Use **Send to tray**. Double-click Bonk's tray icon to open it; right-click to pause or quit.

Closing or minimizing the window keeps Bonk running in the tray. **Quit** in the tray menu exits completely.

### The starter bindings

| Gesture | Action |
| --- | --- |
| Single tap | Media play / pause |
| Double tap | `Ctrl+Shift+M` |
| Triple tap | `Ctrl+Shift+D` |

New installations start in test mode. Editing bindings temporarily suspends detection so your changes do not trigger macros.

### Discord setup

In the **Discord desktop app**, open **User Settings → Keybinds → Add a Keybind**. Set **Toggle Mute** to `Ctrl+Shift+M` and **Toggle Deafen** to `Ctrl+Shift+D`, or choose your own matching shortcuts in Discord and Bonk. Custom Discord keybinds let the shortcut work while another app has focus. [Discord's keybind guide](https://support.discord.com/hc/en-us/articles/217083547-How-do-I-add-different-Keybinds) covers setup.

Bonk sends ordinary Windows input; it does not connect to your Discord account. Other keyboard bindings go to the active application unless that application registers a global keybind.

## Finding your tap

The amber graph shows recent input peaks, the dashed line shows the trigger threshold, and the green line shows the learned noise floor. The graph uses a square-root scale to make quiet levels easier to see; the meter and numeric values use the original amplitude.

Try a normal conversation and a few taps with test mode on. If conversation triggers gestures, reduce sensitivity or raise **Minimum strength**. If taps are missed, lower minimum strength or increase sensitivity. Allow half a second for the detector to settle after changing settings.

Default timing is a 100 ms debounce, a 400 ms maximum gap, and a 520 ms completion timeout after the last tap. The timeout leaves room for a later tap to decay before resolving the sequence. Single taps wait too, so the first tap of a double tap cannot pause your music. Four or more taps in one sequence are ignored.

Detection depends on your microphone, mounting, gain, and room. Keyboard thumps and other sharp sounds can resemble taps. Heavy noise suppression may remove them. This first version uses time-domain transients rather than sound classification; tune it on your actual setup. Gentle taps are plenty.

## Actions

| Action | How to configure it |
| --- | --- |
| Keyboard | Type a shortcut, or press a modified chord/function key in the editor to record it. Example: `Ctrl+Shift+M`. |
| Media | Select play/pause, next/previous track, stop, volume up/down, or mute. |
| Mouse | Select left, right, middle, back, or forward. Clicks use the current pointer position. |
| Launch application | Enter a path and optional arguments. Paths containing spaces and environment variables are supported. |
| Run command | Enter a Windows PowerShell command. It runs without loading your profile and without opening a console. |

Use commands you intend to run. The app does not automatically elevate actions. The log confirms that an application or command was started; it does not collect its output or report its eventual exit status.

## Settings and startup

Settings are saved automatically to:

```text
%AppData%\Bonk\config.json
```

Writes replace the file atomically. If a config is unreadable or malformed, Bonk backs up the original before using defaults. Logs explain save errors. A disconnected selected microphone stays selected; Bonk waits for it instead of switching to another device.

**Start with Windows** adds a `Bonk` entry to the current user's startup registry key and opens the app in the tray. Disable the option before moving or deleting a portable build, or re-enable it from the new location.

For an isolated configuration during development:

```powershell
Bonk.exe --config C:\path\to\another-config.json
```

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows, then:

```powershell
git clone https://github.com/liuYousefKahwaji/Bonk.git
cd Bonk
dotnet build Bonk.slnx --configuration Release
dotnet test Bonk.slnx --configuration Release
dotnet run --project src/Bonk.App
```

To make a portable zip with the runtime included:

```powershell
.\scripts\Publish.ps1
```

The result is `artifacts\Bonk-0.1.0-win-x64.zip`. Extract the entire archive together. The GitHub Actions workflow also builds, tests, and uploads a portable zip for each push or pull request.

### Project layout

```text
src/Bonk.Core/       Audio analysis, tap detection, gestures, bindings, JSON settings
src/Bonk.App/        WPF interface, WASAPI capture, Windows input, tray and startup
tests/              Deterministic detection, timing, config and shortcut tests
scripts/            Portable packaging and icon generation
```

The app uses C#, WPF, .NET 10, and [NAudio.Wasapi 3.1](https://www.nuget.org/packages/NAudio.Wasapi/3.1.0). Audio is processed in reusable approximately 10 ms frames. The capture thread never touches the UI, and action execution stays off the capture thread.

## If something is being stubborn

- **No microphone input:** check the selected device and Windows microphone access for desktop apps. Bonk retries unavailable devices every two seconds.
- **The log shows gestures but nothing happens:** turn test mode off, enable the binding, and verify the shortcut manually in the target app.
- **Discord only responds while focused:** add matching custom keybinds in the Discord desktop app.
- **An elevated app ignores input:** Windows can block input across privilege levels. Use **Restart as administrator** only if your target app needs it. Bonk normally runs without administrator rights. [Windows input documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
- **Single taps feel delayed:** that is the completion timeout giving a double/triple tap time to arrive. Shorten the maximum gap and timeout together if your tapping is quicker.

The core is covered by synthetic audio tests, including ringing, loud sustained audio, changing background noise, and the double-tap shortcut pipeline. Physical tap reliability still needs checking on your headset and in your room.
