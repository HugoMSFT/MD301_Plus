using System;

namespace MD301Plus
{
    public enum MouseButtonTrigger
    {
        None,
        Back,
        Forward,
        Middle
    }

    public sealed class TriggerBinding
    {
        public MouseButtonTrigger MouseButton { get; private set; }
        public KeyboardShortcut Keyboard { get; private set; }
        public string Display { get; private set; }
        public string Description { get; private set; }
        public bool IsAssigned { get { return Keyboard != null || MouseButton != MouseButtonTrigger.None; } }

        private TriggerBinding() { }

        public static TriggerBinding Parse(string text)
        {
            string value = text == null ? "" : text.Trim();
            if (value.Equals("None", StringComparison.OrdinalIgnoreCase))
                return new TriggerBinding { Display = "None", Description = "Not assigned" };
            if (value.Equals("Mouse4", StringComparison.OrdinalIgnoreCase))
                return Mouse(MouseButtonTrigger.Back);
            if (value.Equals("Mouse5", StringComparison.OrdinalIgnoreCase))
                return Mouse(MouseButtonTrigger.Forward);
            if (value.Equals("MiddleMouse", StringComparison.OrdinalIgnoreCase))
                return Mouse(MouseButtonTrigger.Middle);
            KeyboardShortcut shortcut = KeyboardShortcut.Parse(value, true);
            return new TriggerBinding
            {
                Keyboard = shortcut, Display = shortcut.Display, Description = "Keyboard: " + shortcut.Display
            };
        }

        public static TriggerBinding Mouse(MouseButtonTrigger button)
        {
            switch (button)
            {
                case MouseButtonTrigger.Back:
                    return new TriggerBinding { MouseButton = button, Display = "Mouse4", Description = "Back / button 4" };
                case MouseButtonTrigger.Forward:
                    return new TriggerBinding { MouseButton = button, Display = "Mouse5", Description = "Forward / button 5" };
                case MouseButtonTrigger.Middle:
                    return new TriggerBinding { MouseButton = button, Display = "MiddleMouse", Description = "Middle button" };
                default:
                    throw new ArgumentOutOfRangeException("button");
            }
        }

        public bool SameAs(TriggerBinding other)
        {
            return other != null && MouseButton == other.MouseButton &&
                (Keyboard == null ? other.Keyboard == null : Keyboard.SameAs(other.Keyboard));
        }
    }
}
