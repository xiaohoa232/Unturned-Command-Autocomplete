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
        private float wantedX, wantedY;

        public void Place(float parentWidth, float parentHeight, float panelWidth, float minimumHeight, float defaultX, float defaultY)
        {
            X = Clamp(HasCustomPosition ? wantedX : defaultX, parentWidth - panelWidth);
            Y = Clamp(HasCustomPosition ? wantedY : defaultY, parentHeight - minimumHeight);
        }

        public bool BeginDrag(float cursorX, float cursorY)
        {
            if (Pinned) return false;
            grabX = cursorX - X; grabY = cursorY - Y;
            wantedX = X; wantedY = Y;
            HasCustomPosition = true; Dragging = true;
            return true;
        }

        public void MoveDrag(float cursorX, float cursorY)
        {
            if (!Dragging || Pinned) return;
            wantedX = cursorX - grabX; wantedY = cursorY - grabY;
        }

        public void EndDrag()
        {
            if (!Dragging) return;
            wantedX = X; wantedY = Y;
            Dragging = false;
        }

        public void TogglePin()
        {
            if (!HasCustomPosition)
            {
                wantedX = X; wantedY = Y;
                HasCustomPosition = true;
            }
            EndDrag();
            Pinned = !Pinned;
        }

        private static float Clamp(float value, float maximum)
        {
            return Math.Max(0, Math.Min(value, Math.Max(0, maximum)));
        }
    }
}
