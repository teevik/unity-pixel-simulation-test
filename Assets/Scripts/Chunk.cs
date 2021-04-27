using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace PixelTest
{
    public struct Chunk : IDisposable
    {
        public bool RequiresRedraw;
        private IntPtr cellTypesPtr;
        private IntPtr cellColorsPtr;
        private int chunkSize;

        public IntPtr CellColorsPtr => cellColorsPtr;

        public unsafe Chunk(int chunkSize)
        {
            this.chunkSize = chunkSize;
            
            RequiresRedraw = false;
            cellTypesPtr = Marshal.AllocHGlobal(sizeof(CellType) * chunkSize * chunkSize);
            cellColorsPtr = Marshal.AllocHGlobal(sizeof(Color) * chunkSize * chunkSize);
        }
        
        private unsafe Cell GetCell(int index)
        {
            var cellTypePtr = (CellType*)cellTypesPtr + index;
            var cellColorPtr = (Color*)cellColorsPtr + index;

            return new Cell
            {
                Type = *cellTypePtr,
                Color = *cellColorPtr
            };
        }

        private unsafe void SetCell(int index, Cell cell)
        {
            var cellTypePtr = (CellType*)cellTypesPtr + index;
            var cellColorPtr = (Color*)cellColorsPtr + index;

            RequiresRedraw = true;
            *cellTypePtr = cell.Type;
            *cellColorPtr = cell.Color;
        }
        
        public Cell this[int2 cellPosition]
        {
            get
            {
                var index = cellPosition.x + cellPosition.y * chunkSize;
                return GetCell(index);
            }
            set
            {
                var index = cellPosition.x + cellPosition.y * chunkSize;
                SetCell(index, value);
            }
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(cellTypesPtr);
            Marshal.FreeHGlobal(cellColorsPtr);
        }
    }
}