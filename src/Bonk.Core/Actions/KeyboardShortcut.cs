namespace Bonk.Core.Actions;

public static class KeyboardShortcut
{
    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x11, ["Control"] = 0x11, ["Shift"] = 0x10, ["Alt"] = 0x12, ["Win"] = 0x5B,
        ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Space"] = 0x20, ["Tab"] = 0x09,
        ["Escape"] = 0x1B, ["Esc"] = 0x1B, ["Backspace"] = 0x08, ["Delete"] = 0x2E,
        ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21,
        ["PageDown"] = 0x22, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["CapsLock"] = 0x14, ["PrintScreen"] = 0x2C, ["ScrollLock"] = 0x91, ["Pause"] = 0x13,
        ["NumLock"] = 0x90, ["Multiply"] = 0x6A, ["Add"] = 0x6B, ["Subtract"] = 0x6D,
        ["Decimal"] = 0x6E, ["Divide"] = 0x6F, ["Semicolon"] = 0xBA, ["Equals"] = 0xBB,
        ["Comma"] = 0xBC, ["Minus"] = 0xBD, ["Period"] = 0xBE, ["Slash"] = 0xBF,
        ["Backtick"] = 0xC0, ["LeftBracket"] = 0xDB, ["Backslash"] = 0xDC,
        ["RightBracket"] = 0xDD, ["Quote"] = 0xDE
    };

    public static bool TryParse(string? shortcut, out ushort[] keys, out string? error)
    {
        keys = [];
        error = null;
        if (string.IsNullOrWhiteSpace(shortcut)) { error = "Enter a shortcut, such as Ctrl+Shift+M."; return false; }
        var parsed = new List<ushort>();
        foreach (var token in shortcut.Split('+', StringSplitOptions.TrimEntries))
        {
            ushort key;
            if (NamedKeys.TryGetValue(token, out var named)) key = named;
            else if (token.Length == 1 && char.IsAsciiLetterOrDigit(token[0])) key = char.ToUpperInvariant(token[0]);
            else if (token.StartsWith('F') && int.TryParse(token.AsSpan(1), out var f) && f is >= 1 and <= 24) key = (ushort)(0x70 + f - 1);
            else if (token.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && int.TryParse(token.AsSpan(6), out var n) && n is >= 0 and <= 9) key = (ushort)(0x60 + n);
            else { error = $"Unknown key '{token}'. Use letters, numbers, F1–F24, or named keys such as Space."; return false; }
            if (parsed.Contains(key)) { error = "A shortcut cannot contain the same key twice."; return false; }
            parsed.Add(key);
        }
        if (parsed.All(IsModifier)) { error = "Add a regular key to the shortcut."; return false; }
        if (parsed.Count(k => !IsModifier(k)) != 1) { error = "Use modifiers with one regular key."; return false; }
        keys = parsed.OrderBy(k => IsModifier(k) ? 0 : 1).ToArray();
        return true;
    }

    public static bool IsModifier(ushort key) => key is 0x10 or 0x11 or 0x12 or 0x5B;
}
