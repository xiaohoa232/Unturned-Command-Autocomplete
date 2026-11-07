using System;

namespace UnturnedCommandAutocomplete
{
    internal sealed class CandidateViewport
    {
        public int First { get; private set; }
        public int Revision { get; private set; }
        public bool Dragging { get; private set; }
        public int VisibleCount { get { return Math.Min(count, capacity); } }
        public bool CanScroll { get { return MaxFirst > 0; } }

        private int count, capacity;
        private float wheelRemainder, grabOffset;
        private int MaxFirst { get { return count - VisibleCount; } }

        public void Reset()
        {
            SetFirst(0);
            EndPointerInteraction();
        }

        public void Resize(int rowCount, int rowCapacity)
        {
            count = rowCount;
            capacity = rowCapacity;
            SetFirst(First);
            if (!CanScroll) EndPointerInteraction();
        }

        public void Reveal(int selected)
        {
            if (selected < 0) return;
            if (selected < First) SetFirst(selected);
            else if (selected >= First + VisibleCount) SetFirst(selected - VisibleCount + 1);
        }

        public void ScrollWheel(float notches)
        {
            if (!CanScroll || notches == 0) return;
            wheelRemainder += notches;
            int steps = (int)wheelRemainder;
            wheelRemainder -= steps;
            SetFirst(First - steps);
            if ((First == 0 && wheelRemainder > 0) || (First == MaxFirst && wheelRemainder < 0))
                wheelRemainder = 0;
        }

        public float ThumbHeight(float trackHeight)
        {
            return Math.Min(trackHeight, Math.Max(12f, trackHeight * VisibleCount / count));
        }

        public float ThumbTop(float trackHeight)
        {
            return (trackHeight - ThumbHeight(trackHeight)) * First / MaxFirst;
        }

        public void BeginDrag(float pointerY, float trackHeight)
        {
            float height = ThumbHeight(trackHeight), top = ThumbTop(trackHeight);
            grabOffset = pointerY >= top && pointerY < top + height ? pointerY - top : height / 2;
            Dragging = true;
            wheelRemainder = 0;
            MoveDrag(pointerY, trackHeight);
        }

        public void MoveDrag(float pointerY, float trackHeight)
        {
            float travel = trackHeight - ThumbHeight(trackHeight);
            float fraction = Math.Max(0f, Math.Min(1f, (pointerY - grabOffset) / travel));
            SetFirst((int)Math.Round(fraction * MaxFirst));
        }

        public void EndPointerInteraction()
        {
            Dragging = false;
            wheelRemainder = 0;
        }

        private void SetFirst(int value)
        {
            int clamped = Math.Max(0, Math.Min(value, MaxFirst));
            if (clamped == First) return;
            First = clamped;
            Revision++;
        }
    }
}
