using System.IO;
using Newtonsoft.Json;

namespace SOM_Kohonen_WpfApp.Service
{
    public static class JsonIOService
    {
        public static void WriteObjectToJson<T>(string filePath, T obj)
        {
            string json = JsonConvert.SerializeObject(obj, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        public static T ReadObjectFromJson<T>(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
