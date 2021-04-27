using UnityEngine;

namespace PixelTest.Cells
{
    public static class StoneCell
    {
        public static Cell Create()
        {
            return new Cell
            {
                Type = CellType.Stone,
                Color = new Color(0.5f, 0.5f, 0.5f, 1f)
            };
        }
    }
}