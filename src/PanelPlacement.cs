using System;

namespace UnturnedCommandAutocomplete
{
    // Coordinates are in the parent's logical UI units, independent of screen scaling.
    public sealed class PanelPlacement
    {
        public bool Pinned { get; private set; }
        public bool HasCustomPosition { get; private set; }
        public bool Dragging { get; private set; }
        public float X { get; private set; }
        public float Y { get; private set; }
        private float grabX, grabY;

        public void Place(float parentWidth, float parentHeight, float panelWidth, float minimumHeight, float defaultX, float defaultY)
        {
            if (!HasCustomPosition) { X = defaultX; Y = defaultY; }
            X = Clamp(X, parentWidth - panelWidth);
            Y = Clamp(Y, parentHeight - minimumHeight);
        }

        public bool BeginDrag(float cursorX, float cursorY)
        {
            if (Pinned) return false;
            grabX = cursorX - X; grabY = cursorY - Y;
            HasCustomPosition = true; Dragging = true;
            return true;
        }

        public void MoveDrag(float cursorX, float cursorY)
        {
            if (!Dragging || Pinned) return;
            X = cursorX - grabX; Y = cursorY - grabY;
        }

        public void EndDrag() { Dragging = false; }

        public void TogglePin()
        {
            Pinned = !Pinned;
            // Lock the currently displayed position, including the default placement.
            HasCustomPosition = true;
            EndDrag();
        }

        private static float Clamp(float value, float maximum)
        {
            return Math.Max(0, Math.Min(value, Math.Max(0, maximum)));
        }
    }
}
