using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SOM_Kohonen_WpfApp.Models
{
    [Serializable()]
    public class DataCollection : Collection<DataModel>
    {
        public DataCollection()
        {

        }

        public DataCollection(Dictionary<string, object> models)
        {
            foreach (var item in models)
            {
                Add(new DataModel
                {
                    Key = item.Key,
                    Value = item.Value
                });
            }
        }
        public virtual double DistanceToSquared(DataCollection other)
        {
            double total = 0;
            double difference;

            for (int i = 0; i < Count; i++)
            {
                difference = this[i].GetDoubleValue() - other[i].GetDoubleValue();
                total += difference * difference;
            }

            return total;
        }

        public double GetDoubleValue(string key)
        {
            if (this.FirstOrDefault(x => x.Key == key) is DataModel model)
            {
                return model.GetDoubleValue();
            }
            else
            {
                return 0;
            }
        }
    }
}
