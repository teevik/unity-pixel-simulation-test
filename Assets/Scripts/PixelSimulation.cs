using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using PixelTest.Cells;
using PixelTest.Extensions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using static Unity.Mathematics.math;

namespace PixelTest
{
    public class PixelSimulation : MonoBehaviour
    {
        public const int ChunkSize = 64;
        private const int colorStride = 16;
        
        private class ChunkInformation : IDisposable
        {
            public MeshRenderer Renderer;
            public RenderTexture OutputRenderTexture;

            public void Dispose()
            {
                Destroy(Renderer);
                OutputRenderTexture.Release();
            }
        }
    
        [SerializeField] private ComputeShader computeShader;
        [SerializeField] private float simulationStep;
        [SerializeField] private MeshRenderer chunkPrefab;
        [SerializeField] private float chunkScale;
    
        private Dictionary<int2, ChunkInformation> chunkInformations;
        private NativeHashMap<int2, Chunk> chunks;
        private ComputeBuffer cellColorComputeBuffer;
        private int renderChunkKernelIndex;
        
        private void Start()
        {
            chunkInformations = new Dictionary<int2, ChunkInformation>();
            chunks = new NativeHashMap<int2, Chunk>(0, Allocator.Persistent);
            
            renderChunkKernelIndex = computeShader.FindKernel("render_chunk");
            
            cellColorComputeBuffer = new ComputeBuffer(ChunkSize * ChunkSize, colorStride);
            computeShader.SetBuffer(renderChunkKernelIndex, "cell_colors", cellColorComputeBuffer);

            for (var x = -3; x < 4; x++)
            {
                for (var y = -3; y < 4; y++)
                {
                    var chunkRenderer = Instantiate(chunkPrefab, transform, true);
                    chunkRenderer.transform.position = chunkScale * new Vector3(x, y);
                    chunkRenderer.transform.localScale = new Vector3(chunkScale, chunkScale, 1);
                
                    var outputRenderTexture = new RenderTexture(ChunkSize, ChunkSize, 32)
                    {
                        enableRandomWrite = true,
                        useMipMap = false,
                        filterMode = FilterMode.Point
                    };
                
                    outputRenderTexture.Create();
                    
                    chunkRenderer.material.mainTexture = outputRenderTexture;

                    var chunk = new Chunk(ChunkSize);
                    
                    var chunkContainer = new ChunkInformation
                    {
                        Renderer = chunkRenderer,
                        OutputRenderTexture = outputRenderTexture
                    };
                
                    chunks.Add(int2(x, y), chunk);
                    chunkInformations.Add(int2(x, y), chunkContainer);
                }
            }

            StartCoroutine(UpdateWorldCoroutine());
        }

        private void OnDestroy()
        {
            foreach (var (_, chunkContainer) in chunkInformations) chunkContainer.Dispose();
            cellColorComputeBuffer.Dispose();
            chunks.Dispose();
        }

        private IEnumerator UpdateWorldCoroutine()
        {
            // Ugly hack to set data of a compute buffer using a IntPtr
            var setBufferData = (Action<ComputeBuffer, IntPtr, int, int, int, int>) Delegate.CreateDelegate(typeof(Action<ComputeBuffer, IntPtr, int, int, int, int>), null, typeof(ComputeBuffer).GetMethod("InternalSetNativeData", BindingFlags.Instance | BindingFlags.NonPublic));
            
            var random = new Unity.Mathematics.Random(1);
    
            while (true)
            {
                var minX = int.MaxValue;
                var maxX = int.MinValue;
                var minY = int.MaxValue;
                var maxY = int.MinValue;

                foreach (var key in chunkInformations.Keys)
                {
                    if (key.x < minX) minX = key.x;
                    if (key.x > maxX) maxX = key.x;
                    if (key.y < minY) minY = key.y;
                    if (key.y > maxY) maxY = key.y;
                }

                // One random for every processor core
                var randomGenerator = new NativeArray<Unity.Mathematics.Random>(Environment.ProcessorCount + 1, Allocator.TempJob);
                for (var i = 0; i < randomGenerator.Length; i++)
                {
                    randomGenerator[i] = new Unity.Mathematics.Random((uint)random.NextInt());
                }
                
                for (var i = 0; i < 2; i++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        var startPosition = int2(minX + i, minY + j);
                        var endPosition = int2(maxX - i, maxY - j);
                        
                        var a = new UpdatePixelSimulationJob
                        {
                            Chunks = chunks,
                            StartPosition = startPosition,
                            EndPosition = endPosition,
                            RandomGenerator = randomGenerator
                        };

                        var b = a.Schedule((endPosition.x - startPosition.x) / 2 * (endPosition.y - startPosition.y) / 2, 50);
                        b.Complete();
                    }   
                }

                randomGenerator.Dispose();
                
                for (var x = minX; x <= maxX; x++)
                {
                    for (var y = minY; y <= maxY; y++)
                    {
                        var chunkPosition = int2(x, y);
                        var chunkContainer = chunkInformations[chunkPosition];
                        var chunk = chunks[chunkPosition];
                
                        if (chunk.RequiresRedraw)
                        {
                            setBufferData(cellColorComputeBuffer, chunk.CellColorsPtr, 0, 0, 64 * 64, colorStride);
                            
                            computeShader.SetBuffer(renderChunkKernelIndex, "cell_colors", cellColorComputeBuffer);
                            computeShader.SetTexture(renderChunkKernelIndex, "texture_out", chunkContainer.OutputRenderTexture);
                    
                            computeShader.Dispatch(renderChunkKernelIndex, 8, 8, 1);
                            
                            chunk.RequiresRedraw = false;
                            chunks[chunkPosition] = chunk;
                        }
                    }
                }

                yield return new WaitForSeconds(simulationStep);
            }
        }

    }
    
    [BurstCompile]
    public struct UpdatePixelSimulationJob : IJobParallelFor
    {
        [NativeSetThreadIndex]
        private int threadIndex;
        
        [NativeDisableParallelForRestriction]
        public NativeArray<Unity.Mathematics.Random> RandomGenerator;

        [NativeDisableParallelForRestriction]
        public NativeHashMap<int2, Chunk> Chunks;
        public int2 StartPosition;
        public int2 EndPosition;

        public void Execute(int index)
        {
            var random = RandomGenerator[threadIndex];

            var width = EndPosition.x - StartPosition.x;
            var actualIndex = index * 2;
            
            var chunkY = actualIndex / width;
            var chunkX = actualIndex % width;

            var chunkPosition = StartPosition + int2(chunkX, chunkY);

            var chunk = Chunks[chunkPosition];
            
            for (var x = 0; x < 64; x++)
            {
                for (var y = 0; y < 64; y++)
                {
                    var cellPosition = int2(x, y);

                    chunk[cellPosition] = random.NextBool() ? SandCell.Create() : StoneCell.Create();
                }
            }

            Chunks[chunkPosition] = chunk;
        }
    }
}
