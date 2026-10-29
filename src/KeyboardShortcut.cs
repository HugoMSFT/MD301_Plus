using System;
using System.Collections.Generic;
using System.Globalization;

namespace MD301Plus
{
    public struct KeyStroke
    {
        public ushort Key;
        public bool IsUp;
        public bool IsExtended;

        public KeyStroke(ushort key, bool isUp)
        {
            Key = key;
            IsUp = isUp;
            IsExtended = key == 0x5B || key == 0x5D || key == 0x90 || key == 0x2C ||
                (key >= 0x21 && key <= 0x28) || key == 0x2D || key == 0x2E ||
                (key >= 0xA6 && key <= 0xB7);
        }
    }

    public sealed class KeyboardShortcut
    {
        public const uint Alt = 1;
        public const uint Control = 2;
        public const uint Shift = 4;
        public const uint Windows = 8;

        private static readonly Dictionary<string, ushort> NamedKeys =
            new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
            {
                { "Enter", 0x0D }, { "Tab", 0x09 }, { "Space", 0x20 },
                { "Escape", 0x1B }, { "Backspace", 0x08 }, { "Delete", 0x2E },
                { "Insert", 0x2D }, { "Home", 0x24 }, { "End", 0x23 },
                { "PageUp", 0x21 }, { "PageDown", 0x22 }, { "Left", 0x25 },
                { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 },
                { "Plus", 0xBB }, { "Minus", 0xBD }, { "Comma", 0xBC },
                { "Period", 0xBE }, { "Slash", 0xBF }, { "Backslash", 0xDC },
                { "LeftBracket", 0xDB }, { "RightBracket", 0xDD },
                { "PrintScreen", 0x2C }, { "Pause", 0x13 }, { "CapsLock", 0x14 },
                { "NumLock", 0x90 }, { "ScrollLock", 0x91 },
                { "BrowserBack", 0xA6 }, { "BrowserForward", 0xA7 }, { "BrowserRefresh", 0xA8 },
                { "BrowserStop", 0xA9 }, { "BrowserSearch", 0xAA }, { "BrowserFavorites", 0xAB },
                { "BrowserHome", 0xAC }, { "VolumeMute", 0xAD }, { "VolumeDown", 0xAE },
                { "VolumeUp", 0xAF }, { "MediaNextTrack", 0xB0 }, { "MediaPreviousTrack", 0xB1 },
                { "MediaStop", 0xB2 }, { "MediaPlayPause", 0xB3 }, { "LaunchMail", 0xB4 },
                { "MediaSelect", 0xB5 }, { "LaunchApp1", 0xB6 }, { "LaunchApp2", 0xB7 }
            };

        private static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Mute", "VolumeMute" }, { "PlayPause", "MediaPlayPause" },
                { "NextTrack", "MediaNextTrack" }, { "PreviousTrack", "MediaPreviousTrack" },
                { "PrevTrack", "MediaPreviousTrack" }, { "Stop", "MediaStop" },
                { "LaunchMedia", "MediaSelect" }, { "PrintScr", "PrintScreen" }
            };

        public uint Modifiers { get; private set; }
        public ushort Key { get; private set; }
        public string Display { get; private set; }

        private KeyboardShortcut(uint modifiers, ushort key, string display)
        {
            Modifiers = modifiers;
            Key = key;
            Display = display;
        }

        public static KeyboardShortcut Parse(string text, bool isTrigger)
        {
            if (String.IsNullOrWhiteSpace(text) || text.Length > 100)
                throw new SettingsException("Enter a keyboard shortcut, for example Ctrl+Alt+F8.");
            string[] tokens = text.Split('+');
            uint modifiers = 0;
            ushort key = 0;
            string keyName = null;
            foreach (string raw in tokens)
            {
                string token = raw.Trim();
                string canonical;
                if (Aliases.TryGetValue(token, out canonical)) token = canonical;
                uint modifier = 0;
                switch (token.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": modifier = Control; break;
                    case "ALT": modifier = Alt; break;
                    case "SHIFT": modifier = Shift; break;
                    case "WIN": case "WINDOWS": modifier = Windows; break;
                }
                if (modifier != 0)
                {
                    if ((modifiers & modifier) != 0)
                        throw new SettingsException("A shortcut cannot repeat a modifier: " + text);
                    modifiers |= modifier;
                    continue;
                }
                if (key != 0 || token.Length == 0)
                    throw new SettingsException("Use modifiers plus one key, not a macro sequence: " + text);
                if (token.Length == 1 && ((token[0] >= 'a' && token[0] <= 'z') ||
                    (token[0] >= 'A' && token[0] <= 'Z') || (token[0] >= '0' && token[0] <= '9')))
                {
                    keyName = token.ToUpperInvariant();
                    key = keyName[0];
                }
                else
                {
                    int function;
                    if (token.Length > 1 && (token[0] == 'F' || token[0] == 'f') &&
                        Int32.TryParse(token.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out function) &&
                        function >= 1 && function <= 24)
                    {
                        key = (ushort)(0x70 + function - 1);
                        keyName = "F" + function;
                    }
                    else if (NamedKeys.TryGetValue(token, out key))
                    {
                        foreach (KeyValuePair<string, ushort> entry in NamedKeys)
                            if (entry.Value == key) { keyName = entry.Key; break; }
                    }
                    else
                    {
                        throw new SettingsException("Unsupported key '" + token + "'. Use A-Z, 0-9, F1-F24 or a named key.");
                    }
                }
            }
            if (key == 0)
                throw new SettingsException("A shortcut needs a non-modifier key: " + text);
            if (isTrigger && modifiers == 0 && (key < 0x70 || key > 0x87))
                throw new SettingsException("A global trigger needs a modifier, or a single function key (F1-F24).");
            if (key == 0x2E && (modifiers & (Control | Alt)) == (Control | Alt))
                throw new SettingsException("Windows does not allow this app to send or register Ctrl+Alt+Delete.");
            List<string> display = new List<string>();
            if ((modifiers & Control) != 0) display.Add("Ctrl");
            if ((modifiers & Alt) != 0) display.Add("Alt");
            if ((modifiers & Shift) != 0) display.Add("Shift");
            if ((modifiers & Windows) != 0) display.Add("Win");
            display.Add(keyName);
            return new KeyboardShortcut(modifiers, key, String.Join("+", display));
        }

        public bool SameAs(KeyboardShortcut other)
        {
            return other != null && Key == other.Key && Modifiers == other.Modifiers;
        }

        public List<KeyStroke> GetKeyStrokes()
        {
            List<ushort> keys = new List<ushort>();
            if ((Modifiers & Control) != 0) keys.Add(0x11);
            if ((Modifiers & Alt) != 0) keys.Add(0x12);
            if ((Modifiers & Shift) != 0) keys.Add(0x10);
            if ((Modifiers & Windows) != 0) keys.Add(0x5B);
            keys.Add(Key);
            List<KeyStroke> strokes = new List<KeyStroke>();
            foreach (ushort value in keys) strokes.Add(new KeyStroke(value, false));
            for (int i = keys.Count - 1; i >= 0; i--) strokes.Add(new KeyStroke(keys[i], true));
            return strokes;
        }
    }
}
