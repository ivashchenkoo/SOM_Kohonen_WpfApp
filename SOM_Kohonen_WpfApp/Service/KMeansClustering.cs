using System;
using System.Linq;
using System.Threading;

namespace SOM_Kohonen_WpfApp.Service
{
	public static class KMeansClustering
	{
		public static int[] Cluster(double[][] data, int k, int maxIterations = 100, int? seed = null,
            CancellationToken cancellation = default(CancellationToken))
		{
			if (data == null || data.Length == 0 || k < 1 || maxIterations < 1)
				throw new ArgumentException("Invalid k-means configuration.");
			int n = data.Length;
			int dim = data[0].Length;
			Random rand = seed.HasValue ? new Random(seed.Value) : new Random();
			double[][] centroids = new double[k][];
			for (int i = 0; i < k; i++)
			{
				centroids[i] = (double[])data[rand.Next(n)].Clone();
			}
			int[] labels = Enumerable.Repeat(-1, n).ToArray();
			bool changed = true;
			int iter = 0;
			while (changed && iter < maxIterations)
			{
				cancellation.ThrowIfCancellationRequested();
				changed = false;
				// Assign clusters
				for (int i = 0; i < n; i++)
				{
					cancellation.ThrowIfCancellationRequested();
					int best = 0;
					double bestDist = Distance(data[i], centroids[0]);
					for (int j = 1; j < k; j++)
					{
						double dist = Distance(data[i], centroids[j]);
						if (dist < bestDist)
						{
							bestDist = dist;
							best = j;
						}
					}
					if (labels[i] != best)
					{
						labels[i] = best;
						changed = true;
					}
				}
				// Update centroids
				for (int j = 0; j < k; j++)
				{
					var clusterPoints = Enumerable.Range(0, n).Where(i => labels[i] == j).ToList();
					if (clusterPoints.Count == 0) continue;
					double[] mean = new double[dim];
					foreach (var idx in clusterPoints)
					{
						for (int d = 0; d < dim; d++)
							mean[d] += data[idx][d];
					}
					for (int d = 0; d < dim; d++)
						mean[d] /= clusterPoints.Count;
					centroids[j] = mean;
				}
				iter++;
			}
			return labels;
		}

		private static double Distance(double[] a, double[] b)
		{
			double sum = 0;
			for (int i = 0; i < a.Length; i++)
				sum += (a[i] - b[i]) * (a[i] - b[i]);
			return sum;
		}
	}
}
