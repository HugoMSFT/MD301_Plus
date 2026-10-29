using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace MD301Plus
{
    public sealed class SettingsException : Exception
    {
        public SettingsException(string message) : base(message) { }
        public SettingsException(string message, Exception inner) : base(message, inner) { }
    }

    [DataContract]
    public sealed class RingAction
    {
        [DataMember(Name = "label", IsRequired = true, Order = 0)]
        public string Label { get; set; }

        [DataMember(Name = "kind", IsRequired = true, Order = 1)]
        public string Kind { get; set; }

        [DataMember(Name = "command", IsRequired = true, Order = 2)]
        public string Command { get; set; }

        [DataMember(Name = "icon", IsRequired = true, Order = 3)]
        public string Icon { get; set; }

        public RingAction Copy()
        {
            return new RingAction { Label = Label, Kind = Kind, Command = Command, Icon = Icon };
        }

        public override string ToString()
        {
            return Label;
        }

        public void Validate(int index)
        {
            string prefix = "Action " + (index + 1) + ": ";
            if (String.IsNullOrWhiteSpace(Label) || Label.Length > 24)
                throw new SettingsException(prefix + "use a label of 1-24 characters.");
            if (Array.IndexOf(ActionIcons.Names, Icon) < 0)
                throw new SettingsException(prefix + "choose a supported icon.");
            if (Kind == "shortcut")
            {
                KeyboardShortcut.Parse(Command, false);
            }
            else if (Kind == "launch")
            {
                ValidateLaunchTarget(Command);
            }
            else
            {
                throw new SettingsException(prefix + "kind must be shortcut or launch.");
            }
        }

        public static void ValidateLaunchTarget(string target)
        {
            if (String.IsNullOrWhiteSpace(target) || target.Length > 2048)
                throw new SettingsException("A launch target must be an absolute file/folder path or an HTTP(S) URL.");
            Uri uri;
            if (Uri.TryCreate(target, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
                return;
            try
            {
                string root = Path.GetPathRoot(target);
                if (Path.IsPathRooted(target) && root.Length > 1 &&
                    (root.EndsWith("\\", StringComparison.Ordinal) || root.StartsWith("\\\\", StringComparison.Ordinal)))
                {
                    Path.GetFullPath(target);
                    return;
                }
            }
            catch (ArgumentException ex)
            {
                throw new SettingsException("Invalid launch path: " + ex.Message, ex);
            }
            catch (NotSupportedException ex)
            {
                throw new SettingsException("Invalid launch path: " + ex.Message, ex);
            }
            catch (PathTooLongException ex)
            {
                throw new SettingsException("The launch path is too long.", ex);
            }
            throw new SettingsException("Use an absolute file/folder path or an HTTP(S) URL, not a shell command.");
        }
    }

    [DataContract]
    public sealed class RingSettings
    {
        [DataMember(Name = "version", IsRequired = true, Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "trigger", IsRequired = true, Order = 1)]
        public string Trigger { get; set; }

        [DataMember(Name = "actions", IsRequired = true, Order = 2)]
        public List<RingAction> Actions { get; set; }

        [DataMember(Name = "button", EmitDefaultValue = false, Order = 3)]
        public string Button { get; set; }

        public static RingSettings CreateDefault()
        {
            return new RingSettings
            {
                Version = 1,
                Trigger = "None",
                Actions = new List<RingAction>
                {
                    Shortcut("Copy", "Ctrl+C", "copy"),
                    Shortcut("Paste", "Ctrl+V", "paste"),
                    Shortcut("Cut", "Ctrl+X", "cut"),
                    Shortcut("Undo", "Ctrl+Z", "undo"),
                    Shortcut("Redo", "Ctrl+Y", "redo"),
                    Shortcut("Save", "Ctrl+S", "save"),
                    Shortcut("Task view", "Win+Tab", "task-view"),
                    Shortcut("Screenshot", "Win+Shift+S", "screenshot")
                }
            };
        }

        private static RingAction Shortcut(string label, string command, string icon)
        {
            return new RingAction { Label = label, Kind = "shortcut", Command = command, Icon = icon };
        }

        public static List<RingAction> Preset(string name)
        {
            switch (name)
            {
                case "Productivity": return CreateDefault().Actions;
                case "Media":
                    return new List<RingAction>
                    {
                        Shortcut("Play / pause", "MediaPlayPause", "play-pause"),
                        Shortcut("Next track", "MediaNextTrack", "next-track"),
                        Shortcut("Previous track", "MediaPreviousTrack", "previous-track"),
                        Shortcut("Volume up", "VolumeUp", "volume-up"),
                        Shortcut("Volume down", "VolumeDown", "volume-down"),
                        Shortcut("Mute", "VolumeMute", "mute"),
                        Shortcut("Stop", "MediaStop", "stop"),
                        Shortcut("Media app", "MediaSelect", "launch")
                    };
                case "Windows":
                    return new List<RingAction>
                    {
                        Shortcut("Task view", "Win+Tab", "task-view"),
                        Shortcut("Desktop", "Win+D", "windows"),
                        Shortcut("Explorer", "Win+E", "folder"),
                        Shortcut("Settings", "Win+I", "shortcut"),
                        Shortcut("Clipboard", "Win+V", "paste"),
                        Shortcut("Emoji", "Win+Period", "shortcut"),
                        Shortcut("Quick settings", "Win+A", "shortcut"),
                        Shortcut("Screenshot", "Win+Shift+S", "screenshot")
                    };
                default: throw new SettingsException("Unknown action preset: " + name);
            }
        }

        public RingSettings Copy()
        {
            RingSettings result = new RingSettings
            {
                Version = Version, Trigger = Trigger, Button = Button, Actions = new List<RingAction>()
            };
            foreach (RingAction action in Actions)
                result.Actions.Add(action.Copy());
            return result;
        }

        public void Validate()
        {
            if (Version != 1)
                throw new SettingsException("Unsupported settings version. Expected version 1.");
            TriggerBinding trigger = TriggerBinding.Parse(Trigger);
            if (Button != null && Button != "dpi" && Button != "keyboard")
                throw new SettingsException("Unsupported button assignment.");
            if (Button != null && trigger.Keyboard == null)
                throw new SettingsException("The selected top/keyboard button needs a keyboard mapping.");
            if (Button == "dpi" && (trigger.Keyboard.Modifiers != 0 ||
                trigger.Keyboard.Key < 0x70 || trigger.Keyboard.Key > 0x7A))
                throw new SettingsException("Map the DPI button to one function key, F1-F11, in Gear Link.");
            if (Actions == null || Actions.Count != 8)
                throw new SettingsException("The ring must contain exactly eight actions.");
            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i] == null)
                    throw new SettingsException("Action " + (i + 1) + " is missing.");
                Actions[i].Validate(i);
                if (Actions[i].Kind == "shortcut" &&
                    KeyboardShortcut.Parse(Actions[i].Command, false).SameAs(trigger.Keyboard))
                    throw new SettingsException("Action " + (i + 1) + " cannot send the ring's own trigger.");
            }
        }
    }

    public static class ActionIcons
    {
        public static readonly string[] Names =
        {
            "copy", "paste", "cut", "undo", "redo", "save", "task-view",
            "screenshot", "search", "folder", "launch", "shortcut",
            "play-pause", "next-track", "previous-track", "volume-up", "volume-down", "mute", "stop", "windows"
        };

        public static string Glyph(string name)
        {
            switch (name)
            {
                case "copy": return "\uE8C8";
                case "paste": return "\uE77F";
                case "cut": return "\uE8C6";
                case "undo": return "\uE7A7";
                case "redo": return "\uE7A6";
                case "save": return "\uE74E";
                case "task-view": return "\uE7C4";
                case "screenshot": return "\uE722";
                case "search": return "\uE721";
                case "folder": return "\uE8B7";
                case "launch": return "\uE8A7";
                case "shortcut": return "\uE765";
                case "play-pause": return "\uE768";
                case "next-track": return "\uE893";
                case "previous-track": return "\uE892";
                case "volume-up": return "\uE767";
                case "volume-down": return "\uE993";
                case "mute": return "\uE74F";
                case "stop": return "\uE71A";
                case "windows": return "\uE782";
                default: throw new SettingsException("Unsupported action icon: " + name);
            }
        }
    }

    public sealed class SettingsStore
    {
        public string FilePath { get; private set; }

        public SettingsStore(string path)
        {
            FilePath = Path.GetFullPath(path);
        }

        public RingSettings Load()
        {
            if (!File.Exists(FilePath))
                return RingSettings.CreateDefault();
            if (new FileInfo(FilePath).Length > 131072)
                throw new SettingsException("The settings file is larger than 128 KB. It has not been changed.");
            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    RingSettings settings = (RingSettings)new DataContractJsonSerializer(typeof(RingSettings)).ReadObject(stream);
                    if (settings == null)
                        throw new SettingsException("The settings file must contain an object.");
                    settings.Validate();
                    return settings;
                }
            }
            catch (SerializationException ex)
            {
                throw new SettingsException("Invalid settings JSON. The original file has not been changed: " + FilePath, ex);
            }
        }

        public void Save(RingSettings settings)
        {
            settings.Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(RingSettings)).WriteObject(stream, settings);
                    stream.Flush(true);
                }
                if (File.Exists(FilePath))
                    RetryFileOperation(delegate { File.Replace(temporary, FilePath, null); });
                else
                    RetryFileOperation(delegate { File.Move(temporary, FilePath); });
            }
            finally
            {
                if (File.Exists(temporary))
                    RetryFileOperation(delegate { File.Delete(temporary); });
            }
        }

        private static void RetryFileOperation(Action operation)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { operation(); return; }
                catch (IOException error)
                {
                    int code = error.HResult & 0xFFFF;
                    if (attempt == 4 || (code != 32 && code != 33 && code != 1175)) throw;
                    // Indexers and antivirus can briefly open a file without delete-sharing.
                    Thread.Sleep(20 * (attempt + 1));
                }
            }
        }
    }
}
