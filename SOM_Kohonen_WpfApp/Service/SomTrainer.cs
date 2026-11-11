using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SOM_Kohonen_WpfApp.Models;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Service
{
    public static class SomTrainer
    {
        public static int[] SourceOrder(int rowCount) => Enumerable.Range(0, rowCount).ToArray();

        // One iteration is a complete pass over the supplied, immutable row order.
        public static void Train(Map map, IList<DataCollection> rows, int[] order, int iterations,
            double rate, double radius, CancellationToken cancellation, Action<int> progress = null)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                cancellation.ThrowIfCancellationRequested();
                double fraction = (double)iteration / iterations;
                double currentRadius = radius * Math.Exp(-fraction * Math.Log(radius));
                double radiusSquared = currentRadius * currentRadius;
                double learningRate = rate * Math.Exp(-fraction);
                foreach (int index in order)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var row = rows[index];
                    var bmu = map.GetBestMatchingNode(row);
                    // Scan the whole lattice: rectangular clipping can exclude hex neighbours.
                    for (int x = 0; x < map.Width; x++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        for (int y = 0; y < map.Height; y++)
                        {
                            var node = map[x, y];
                            double distance = bmu.DistanceToSquared(node, map.NodeType);
                            if (distance <= radiusSquared)
                                node.AdjustWeights(row, learningRate, Math.Exp(-distance / (2 * radiusSquared)));
                        }
                    }
                }
                progress?.Invoke(iteration + 1);
            }
        }
    }
}
