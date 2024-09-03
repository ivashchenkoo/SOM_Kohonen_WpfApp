using System;

namespace SOM_Kohonen_WpfApp.Service
{
	public class RandomGenerator
	{
		private int _seed;

		public RandomGenerator(int seed) => _seed = seed;

		public double Next()
		{
			var x = Math.Sin(_seed++) * 10000;
			return x - Math.Floor(x);
		}
	}
}
