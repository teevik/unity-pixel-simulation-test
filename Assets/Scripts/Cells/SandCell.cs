using UnityEngine;

namespace PixelTest.Cells
{
    public static class SandCell
    {
        public static Cell Create()
        {
            return new Cell
            {
                Type = CellType.Sand,
                Color = new Color(1f, 1f, 0.5f, 1f)
            };
        }
    }
}