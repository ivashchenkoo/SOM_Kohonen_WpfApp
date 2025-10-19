using System;
using SOM_Kohonen_WpfApp.Models;

namespace SOM_Kohonen_WpfApp.SOM
{
	[Serializable()]
	public class Node
	{
		public Node(int x, int y, DataCollection weights)
		{
			X = x;
			Y = y;
			Weights = new DataCollection();
			foreach (DataModel weight in weights)
			{
				Weights.Add(new DataModel
				{
					Key = weight.Key,
					Value = weight.GetDoubleValue(),
					MaxCollectionValue = weight.MaxCollectionValue
				});
			}
		}

		public DataCollection Weights { get; private set; }

		/// <summary>
		/// Contains the X value of this node in the Map.
		/// </summary>
		public int X { get; private set; }

		/// <summary>
		/// Contains the Y value of this node in the Map.
		/// </summary>
		public int Y { get; private set; }

		/// <summary>
		/// Returns the distance between this Node and the specified Node.
		/// Based on the standard Euclidean distance function for squares.
		/// </summary>
		public double DistanceToSquared(Node node, NodeType nodeType = NodeType.Square)
		{
			int differenceX = X - node.X;
			int differenceY = Y - node.Y;
			if (nodeType == NodeType.Hexagonal)
			{
				// Hex grid distance (cube coordinates)
				// Convert (x, y) to cube coordinates
				int x1 = X - (Y / 2);
				int z1 = Y;
				int y1 = -x1 - z1;
				int x2 = node.X - (node.Y / 2);
				int z2 = node.Y;
				int y2 = -x2 - z2;
				return Math.Pow(Math.Max(Math.Abs(x1 - x2), Math.Max(Math.Abs(y1 - y2), Math.Abs(z1 - z2))), 2);
			}
			return (differenceX * differenceX) + (differenceY * differenceY);
		}

		/// <summary>
		/// Adjusts the weights in this Node based on the weights in the specified weights.
		/// </summary>
		public void AdjustWeights(DataCollection input, double learningRate, double fallOfDistance)
		{
			for (int i = 0; i < Weights.Count; i++)
			{
				Weights[i].Value = Weights[i].GetDoubleValue() + (learningRate * fallOfDistance * (input.GetDoubleValue(Weights[i].Key) - Weights[i].GetDoubleValue()));
			}
		}
	}
}
