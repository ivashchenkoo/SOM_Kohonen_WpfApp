using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SOM_Kohonen_WpfApp.Models;

namespace SOM_Kohonen_WpfApp.SOM
{
    [Serializable()]
    public class Map
    {
        public Map(int width, int height)
        {
            Grid = new Node[width, height];
        }

        #region Properties
        /// <summary>
        /// The grid of Nodes contained in this Map.
        /// </summary>
        public Node[,] Grid { get; set; }

        /// <summary>
        /// The depth of the Map.
        /// </summary>
        public int Depth { get; set; }

        /// <summary>
        /// Gets the width of the Map.
        /// </summary>
        public int Width
        {
            get { return this.Grid.GetLength(0); }
        }

        /// <summary>
        /// Gets the height of the Map.
        /// </summary>
        public int Height
        {
            get { return this.Grid.GetLength(1); }
        }

        /// <summary>
        /// Gets or sets the Node at the specified position by X and Y.
        /// </summary>
        public Node this[int x, int y]
        {
            get { return Grid[x, y]; }
            set { Grid[x, y] = value; }
        }
        #endregion

        /// <summary>
        /// Trains a self-organized map using the specified training data.
        /// </summary>
        public virtual void Train(IList<DataCollection> models, List<string> columns, double learningRateStart = 0.05, int iterations = 100)
        {
            DataDictionary maxValues = new DataDictionary();
            foreach (var column in columns)
            {
                maxValues.Add(column, models.Max(x => x.GetDoubleValue(column)));
            }

            DataDictionary minValues = new DataDictionary();
            foreach (var column in columns)
            {
                minValues.Add(column, models.Min(x => x.GetDoubleValue(column)));
            }

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    DataCollection weights = new DataCollection();

                    for (int i = 0; i < columns.Count; i++)
                    {
                        double min = minValues.GetDoubleValue(columns[i]);
                        double max = maxValues.GetDoubleValue(columns[i]);
                        weights.Add(new DataModel
                        {
                            Key = columns[i],
                            Value = (0.5 * (max - min)) + min,
                            MaxCollectionValue = max
                        });
                    }

                    this[x, y] = new Node(x, y, weights);
                }
            }
            
            double latticeRadius = Math.Max(Width, Height) / 2;
            double timeConstant = iterations / Math.Log(latticeRadius);
            double learningRate = learningRateStart;

            int iteration = 0;

            while (iteration < iterations)
            {
                Debug.WriteLine("iteration: {0}, learningRate: {1}", iteration, learningRate);
                double neighborhoodRadius = latticeRadius * Math.Exp(-iteration / timeConstant);
                double neighborhoodDiameter = neighborhoodRadius * 2;
                double neighborhoodRadiusSquared = neighborhoodRadius * neighborhoodRadius;

                // Finds the node that best matches the given training data and adjusts the nodes near it based on the given radius and training rate.
                foreach (DataCollection model in models)
                {
                    Node bestMatchingNode = GetBestMatchingNode(model);

                    // Calculate the bounds of the neighborhood of MapNodes in the vicinity of the best matching node to adjust.
                    int startX = (int)Math.Max(0, bestMatchingNode.X - neighborhoodRadius - 1);
                    int startY = (int)Math.Max(0, bestMatchingNode.Y - neighborhoodRadius - 1);
                    int endX = (int)Math.Min(Width, startX + neighborhoodDiameter + 1);
                    int endY = (int)Math.Min(Height, startY + neighborhoodDiameter + 1);

                    for (int x = startX; x < endX; x++)
                    {
                        for (int y = startY; y < endY; y++)
                        {
                            Node nodeToAdjust = this[x, y];
                            double distanceSquared = bestMatchingNode.DistanceToSquared(nodeToAdjust);

                            // Perform a filter to get only the nodes in the neighborhood
                            if (distanceSquared <= neighborhoodRadiusSquared)
                            {
                                double distanceFalloff = Math.Exp(-distanceSquared / (2 * neighborhoodRadiusSquared));
                                nodeToAdjust.AdjustWeights(model, learningRate, distanceFalloff);
                            }
                        }
                    }
                }

                iteration++;
                learningRate = learningRateStart * Math.Exp(-(double)iteration / iterations);
            }
        }

        /// <summary>
        /// Goes through the entire map and find the node whose data best matches the given data.
        /// </summary>
        public Node GetBestMatchingNode(DataCollection dataToMatch)
        {
            Node bestMatchingNode = this[0, 0];
            double bestDistance = double.MaxValue;
            double currentDistance;
            Node currentNode;

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    currentNode = this[x, y];
                    currentDistance = dataToMatch.DistanceToSquared(currentNode.Weights);

                    if (currentDistance < bestDistance)
                    {
                        bestMatchingNode = currentNode;
                        bestDistance = currentDistance;
                    }
                }
            }

            return bestMatchingNode;
        }
    }
}
