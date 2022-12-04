using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Service
{
    public static class MapIOService
    {
        private static readonly string fileExtension = ".som";

        public static void SaveToFile(string filePath, Map map)
        {
            using (Stream stream = File.Open(FixFileName(filePath), FileMode.Create))
            {
                BinaryFormatter bFormatter = new BinaryFormatter();
                bFormatter.Serialize(stream, map);
                stream.Close();
            }
        }

        public static Map LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            Map map;
            using (Stream stream = File.Open(filePath, FileMode.Open))
            {
                BinaryFormatter bFormatter = new BinaryFormatter();
                map = (Map)bFormatter.Deserialize(stream);
                stream.Close();
            }

            return map;
        }

        private static string FixFileName(string fileName)
        {
            return fileName.EndsWith(fileExtension) ? fileName : fileName + fileExtension;
        }
    }
}
