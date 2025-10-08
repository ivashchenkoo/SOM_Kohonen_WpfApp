using System;

namespace SOM_Kohonen_WpfApp.Models
{
	[Serializable()]
	public class DataModel
	{
		public string Key { get; set; }
		public object Value { get; set; }
		public double MaxCollectionValue { get; set; }

		public double GetDoubleValue()
		{
			double.TryParse(Value.ToString(), out double doubleValue);
			return doubleValue;
		}
	}
}
