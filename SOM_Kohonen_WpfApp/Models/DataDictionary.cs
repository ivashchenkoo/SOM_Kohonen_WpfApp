using System;
using System.Collections.Generic;
using System.Linq;

namespace SOM_Kohonen_WpfApp.Models
{
	[Serializable()]
	public class DataDictionary : Dictionary<string, object>
	{
		public virtual double GetDoubleValue(int index)
		{
			double.TryParse(this.ElementAt(index).Value.ToString(), out double value);
			return value;
		}

		public virtual double GetDoubleValue(string key)
		{
			if (this.TryGetValue(key, out object value))
			{
				double.TryParse(value.ToString(), out double doubleValue);
				return doubleValue;
			}
			else
			{
				return 0;
			}
		}
	}
}
