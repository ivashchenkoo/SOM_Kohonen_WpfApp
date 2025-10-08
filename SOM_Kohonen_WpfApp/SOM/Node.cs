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
		/// Based on the standard Euclidean distance function.
		/// <returns>
		/// Returns the sum of the squares of the differences between this nodes X and Y to the other nodes X and Y properties.
		/// </returns>
		public double DistanceToSquared(Node node)
		{
			int differenceX = X - node.X;
			int differenceY = Y - node.Y;

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
