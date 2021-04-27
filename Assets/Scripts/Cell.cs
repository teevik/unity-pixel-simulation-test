using UnityEngine;

namespace PixelTest
{
    public enum CellType : byte
    {
        Empty = 0,
        Stone = 1,
        Sand = 2
    }

    public struct Cell
    {
        public CellType Type;
        public Color Color;
    }
}