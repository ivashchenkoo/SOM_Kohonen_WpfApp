using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Service
{
    public static class ComparisonResultFile
    {
        public const string Filter = "Результат порівняння SOM (*.somcmp)|*.somcmp";
        private sealed class Document
        {
            [JsonProperty(Required = Required.Always)]
            public int Version { get; set; }
            [JsonProperty(Required = Required.Always)]
            public ComparisonResult Result { get; set; }
        }

        public static void Save(string path, ComparisonResult result)
        {
            // Finish writing in the destination directory before replacing an earlier result.
            path = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                using (var compressed = new GZipStream(file, CompressionMode.Compress))
                using (var writer = new StreamWriter(compressed, new UTF8Encoding(false)))
                    new JsonSerializer().Serialize(writer, new Document { Version = 1, Result = result });
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static ComparisonResult Open(string path)
        {
            Document document;
            using (var file = File.OpenRead(path))
            using (var compressed = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new JsonTextReader(new StreamReader(compressed, Encoding.UTF8)))
            {
                var serializer = new JsonSerializer { CheckAdditionalContent = true, DateParseHandling = DateParseHandling.None };
                document = serializer.Deserialize<Document>(reader);
            }
            if (document == null || document.Version != 1)
                throw new InvalidDataException("Непідтримувана версія файлу результатів SOM.");
            Validate(document.Result);
            document.Result.Data.RestoreNormalized(document.Result.Config);
            return document.Result;
        }

        private static void Require(bool condition)
        {
            if (!condition) throw new InvalidDataException("Файл містить неповні або несумісні дані результату SOM.");
        }

        private static bool IsVector(double[] values, int size) =>
            values != null && values.Length == size && values.All(ComparisonConfig.Finite);

        private static void Validate(ComparisonResult result)
        {
            Require(result?.Config != null && result.Data != null);
            var config = result.Config;
            var data = result.Data;
            config.Validate();
            int depth = config.Features.Count;
            int size = config.Width * config.Height;
            Require(data.Ids != null && data.Ids.Length >= 2 && !data.Ids.Any(string.IsNullOrWhiteSpace));
            var ids = new HashSet<string>(data.Ids, StringComparer.Ordinal);
            Require(ids.Count == data.Ids.Length && data.Original?.Length == ids.Count &&
                data.Original.All(row => IsVector(row, depth)) && IsVector(data.Offsets, depth) &&
                IsVector(data.Scales, depth) && data.Scales.All(scale => scale > 0));

            var runs = new HashSet<Tuple<int, NodeType>>();
            var timed = new HashSet<Tuple<int, NodeType>>();
            foreach (var timing in result.TrainingTimes)
                Require(timing != null && config.Seeds.Contains(timing.Seed) && Enum.IsDefined(typeof(NodeType), timing.Lattice) &&
                    ComparisonConfig.Finite(timing.Seconds) && timing.Seconds >= 0 && timed.Add(Tuple.Create(timing.Seed, timing.Lattice)));
            foreach (var run in result.Runs)
            {
                Require(run != null && config.Seeds.Contains(run.Seed) && Enum.IsDefined(typeof(NodeType), run.Lattice) &&
                    runs.Add(Tuple.Create(run.Seed, run.Lattice)));
                Require(run.NodeClusters?.Length == size && run.NodeWeights?.Length == size &&
                    run.NodeClusters.All(c => c >= 0 && c < config.K) && run.NodeWeights.All(v => IsVector(v, depth)) &&
                    run.SelectedNodes.All(i => i >= 0 && i < size));
                Require(run.Silhouette != null && run.Profiles?.Count == config.K && run.Assignments?.Count == ids.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var a in run.Assignments)
                    Require(a != null && ids.Contains(a.Id) && seen.Add(a.Id) &&
                        a.BmuX >= 0 && a.BmuX < config.Width && a.BmuY >= 0 && a.BmuY < config.Height &&
                        a.Cluster == run.NodeClusters[a.BmuX * config.Height + a.BmuY]);
                for (int c = 0; c < config.K; c++)
                {
                    var profile = run.Profiles[c];
                    Require(profile != null && profile.Cluster == c && profile.Means?.Length == depth);
                }
            }
            Require(result.Aris.All(a => a != null && runs.Contains(Tuple.Create(a.SeedA, a.LatticeA)) &&
                runs.Contains(Tuple.Create(a.SeedB, a.LatticeB)) && ComparisonConfig.Finite(a.Value)));
        }
    }
}
