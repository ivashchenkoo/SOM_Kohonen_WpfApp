using System;
using System.Collections.Generic;
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
        public virtual void Initialize(IList<DataCollection> models, List<string> columns)
        {
            Depth = columns.Count;
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

            // Train
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
