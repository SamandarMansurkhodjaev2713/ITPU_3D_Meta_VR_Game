using System;
using System.Collections.Generic;

namespace ITPU.Domain
{
    // Paletted CPU reference. Integration uploads only when dirty, at a profiled rate.
    // 256x128 with 12 undo frames uses < 0.5 MiB pixel history. Not a Unity performance claim.
    public sealed class DrawingSurface
    {
        public int Width { get; }
        public int Height { get; }
        public int Revision { get; private set; }

        private readonly byte[] pixels;
        private readonly List<byte[]> history = new List<byte[]>();
        private readonly int maxUndo;
        private bool active;
        private byte[] before;

        public DrawingSurface(int width = 256, int height = 128, int maxUndo = 12)
        {
            if (width < 8 || height < 8 || width > 512 || height > 512 || maxUndo < 1 || maxUndo > 32)
                throw new ArgumentException("Invalid board limits");
            Width = width;
            Height = height;
            this.maxUndo = maxUndo;
            pixels = new byte[width * height];
        }

        public bool Begin()
        {
            if (active) return false;
            before = (byte[])pixels.Clone();
            active = true;
            return true;
        }

        public bool Line(double x0, double y0, double x1, double y1, double radiusPixels, byte color)
        {
            if (!active) return false;
            if (color > 5) throw new ArgumentException("Invalid palette index");
            Guard.Finite(radiusPixels, "radius");
            if (radiusPixels < .5 || radiusPixels > 32) throw new ArgumentException("Invalid radius");
            var ax = Clamp01(x0) * (Width - 1);
            var ay = Clamp01(y0) * (Height - 1);
            var bx = Clamp01(x1) * (Width - 1);
            var by = Clamp01(y1) * (Height - 1);
            var loX = Math.Max(0, (int)Math.Floor(Math.Min(ax, bx) - radiusPixels));
            var hiX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, bx) + radiusPixels));
            var loY = Math.Max(0, (int)Math.Floor(Math.Min(ay, by) - radiusPixels));
            var hiY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, by) + radiusPixels));
            var dx = bx - ax;
            var dy = by - ay;
            var length = dx * dx + dy * dy;
            var changed = false;
            for (var y = loY; y <= hiY; y++)
            for (var x = loX; x <= hiX; x++)
            {
                var t = length == 0
                    ? 0
                    : Guard.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1);
                var errorX = x - (ax + t * dx);
                var errorY = y - (ay + t * dy);
                var index = y * Width + x;
                if (errorX * errorX + errorY * errorY <= radiusPixels * radiusPixels &&
                    pixels[index] != color)
                {
                    pixels[index] = color;
                    changed = true;
                }
            }
            if (changed) Revision++;
            return changed;
        }

        public bool End()
        {
            if (!active) return false;
            active = false;
            var changed = false;
            for (var index = 0; index < pixels.Length; index++)
            {
                if (pixels[index] == before[index]) continue;
                changed = true;
                break;
            }
            if (changed)
            {
                if (history.Count == maxUndo) history.RemoveAt(0);
                history.Add(before);
            }
            before = null;
            return changed;
        }

        public bool Cancel()
        {
            if (!active) return false;
            Array.Copy(before, pixels, pixels.Length);
            before = null;
            active = false;
            Revision++;
            return true;
        }

        public bool Undo()
        {
            if (active || history.Count == 0) return false;
            var index = history.Count - 1;
            Array.Copy(history[index], pixels, pixels.Length);
            history.RemoveAt(index);
            Revision++;
            return true;
        }

        public void CopyPixels(byte[] destination)
        {
            if (destination == null || destination.Length != pixels.Length)
                throw new ArgumentException("Wrong buffer size");
            Array.Copy(pixels, destination, pixels.Length);
        }

        public byte[] Snapshot() => (byte[])pixels.Clone();

        public int NonEmptyPixels()
        {
            var count = 0;
            foreach (var color in pixels)
                if (color != 0) count++;
            return count;
        }

        public void Reset()
        {
            Array.Clear(pixels, 0, pixels.Length);
            history.Clear();
            before = null;
            active = false;
            Revision++;
        }

        private static double Clamp01(double value)
        {
            Guard.Finite(value, "uv");
            return Guard.Clamp(value, 0, 1);
        }
    }
}
