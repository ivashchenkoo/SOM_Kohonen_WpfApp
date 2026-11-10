using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Service
{
    public sealed class SubscriberAssignment
    {
        public string Id { get; set; }
        public int BmuX { get; set; }
        public int BmuY { get; set; }
        public int Cluster { get; set; }
    }
    public sealed class ClusterProfile
    {
        public int Cluster { get; set; }
        public int Count { get; set; }
        public double Share { get; set; }
        public double?[] Means { get; set; }
    }
    public sealed class ComparisonRun
    {
        public int Seed { get; set; }
        public NodeType Lattice { get; set; }
        public string InitialWeightsHash { get; set; }
        public string OrderHash { get; set; }
        public MetricValue Silhouette { get; set; }
        public List<SubscriberAssignment> Assignments { get; set; }
        public List<ClusterProfile> Profiles { get; set; }
    }
    public sealed class AriComparison
    {
        public int SeedA { get; set; }
        public int SeedB { get; set; }
        public NodeType LatticeA { get; set; }
        public NodeType LatticeB { get; set; }
        public double Value { get; set; }
    }
    public sealed class ExperimentProgress
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public int Seed { get; set; }
        public NodeType Lattice { get; set; }
        public string Stage { get; set; }
        public int Iteration { get; set; }
    }
    public sealed class ComparisonResult
    {
        public ComparisonConfig Config { get; set; }
        public PreparedComparisonData Data { get; set; }
        public List<ComparisonRun> Runs { get; } = new List<ComparisonRun>();
        public List<AriComparison> Aris { get; } = new List<AriComparison>();
        public bool Cancelled { get; set; }
        public string Error { get; set; }
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime FinishedUtc { get; set; }
        public int CompletedTrainings { get; set; }
    }

    public static class ComparisonExperiment
    {
        public static int[] CreateOrder(int count, int seed)
        {
            var order = Enumerable.Range(0, count).ToArray();
            var random = new Random(seed);
            for (int i = count - 1; i > 0; i--) { int j = random.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            return order;
        }

        public static Map CopyMap(Map source, NodeType lattice)
        {
            var copy = new Map(source.Width, source.Height, source.Seed, lattice) { Depth = source.Depth };
            for (int x = 0; x < source.Width; x++)
                for (int y = 0; y < source.Height; y++) copy[x, y] = new Node(x, y, source[x, y].Weights);
            return copy;
        }

        public static string WeightsHash(Map map)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                    for (int x = 0; x < map.Width; x++) for (int y = 0; y < map.Height; y++)
                        foreach (var weight in map[x, y].Weights) writer.Write(weight.GetDoubleValue());
                return Hash(stream.ToArray());
            }
        }
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        public static ComparisonResult Run(ComparisonConfig config, PreparedComparisonData data, CancellationToken token,
            IProgress<ExperimentProgress> progress = null)
        {
            config.Validate();
            var result = new ComparisonResult { Config = config, Data = data };
            try
            {
                foreach (int seed in config.Seeds)
                {
                    token.ThrowIfCancellationRequested();
                    var initial = new Map(config.Width, config.Height, seed);
                    initial.Initialize(data.Normalized, config.Features);
                    var square = CopyMap(initial, NodeType.Square);
                    var hex = CopyMap(initial, NodeType.Hexagonal);
                    int[] squareOrder = CreateOrder(data.Ids.Length, seed);
                    int[] hexOrder = (int[])squareOrder.Clone();
                    string weightsHash = WeightsHash(square);
                    if (weightsHash != WeightsHash(hex) || !squareOrder.SequenceEqual(hexOrder) ||
                        ReferenceEquals(square[0, 0].Weights[0], hex[0, 0].Weights[0]))
                        throw new InvalidOperationException("Початкові умови пари не збігаються або ваги спільні.");
                    string orderHash = Hash(squareOrder.SelectMany(BitConverter.GetBytes).ToArray());
                    foreach (var map in new[] { square, hex })
                    {
                        token.ThrowIfCancellationRequested();
                        Action<string, int> report = (stage, iteration) => progress?.Report(new ExperimentProgress
                        { Completed = result.CompletedTrainings, Total = config.Seeds.Length * 2, Seed = seed, Lattice = map.NodeType, Stage = stage, Iteration = iteration });
                        report("Навчання SOM", 0);
                        SomTrainer.Train(map, data.Normalized, map.NodeType == NodeType.Square ? squareOrder : hexOrder,
                            config.Iterations, config.LearningRate, config.Radius, token, iteration => report("Навчання SOM", iteration));
                        result.CompletedTrainings++;
                        report("k-means", config.Iterations);
                        var nodes = new List<Node>();
                        for (int x = 0; x < map.Width; x++) for (int y = 0; y < map.Height; y++) nodes.Add(map[x, y]);
                        var labels = KMeansClustering.Cluster(nodes.Select(n => n.Weights.Select(w => w.GetDoubleValue()).ToArray()).ToArray(),
                            config.K, config.KMeansIterations, config.KMeansSeed, token);
                        for (int i = 0; i < nodes.Count; i++) nodes[i].ClusterId = labels[i];
                        report("Розрахунок показників", config.Iterations);
                        var assignments = new List<SubscriberAssignment>();
                        for (int i = 0; i < data.Ids.Length; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var bmu = map.GetBestMatchingNode(data.Normalized[i]);
                            assignments.Add(new SubscriberAssignment { Id = data.Ids[i], BmuX = bmu.X, BmuY = bmu.Y, Cluster = bmu.ClusterId });
                        }
                        var run = new ComparisonRun { Seed = seed, Lattice = map.NodeType, Assignments = assignments,
                            InitialWeightsHash = weightsHash, OrderHash = orderHash,
                            Silhouette = ComparisonMetrics.Silhouette(data.Normalized, assignments.Select(a => a.Cluster).ToArray(), token),
                            Profiles = new List<ClusterProfile>() };
                        for (int cluster = 0; cluster < config.K; cluster++)
                        {
                            token.ThrowIfCancellationRequested();
                            var members = Enumerable.Range(0, data.Ids.Length).Where(i => assignments[i].Cluster == cluster).ToArray();
                            run.Profiles.Add(new ClusterProfile { Cluster = cluster, Count = members.Length, Share = (double)members.Length / data.Ids.Length,
                                Means = config.Features.Select((f, j) => members.Length == 0 ? (double?)null : members.Average(i => data.Original[i][j])).ToArray() });
                        }
                        var nextAris = new List<AriComparison>();
                        var ids = assignments.ToDictionary(a => a.Id, a => a.Cluster, StringComparer.Ordinal);
                        foreach (var previous in result.Runs.Where(r => r.Lattice == run.Lattice || r.Seed == seed))
                        {
                            token.ThrowIfCancellationRequested();
                            nextAris.Add(new AriComparison { SeedA = previous.Seed, SeedB = seed, LatticeA = previous.Lattice, LatticeB = run.Lattice,
                                Value = ComparisonMetrics.Ari(previous.Assignments.ToDictionary(a => a.Id, a => a.Cluster, StringComparer.Ordinal), ids) });
                        }
                        result.Runs.Add(run); // Commit only a fully evaluated run; retain it on later cancellation.
                        result.Aris.AddRange(nextAris);
                        report("Запуск завершено", config.Iterations);
                    }
                }
            }
            catch (OperationCanceledException) { result.Cancelled = true; }
            catch (Exception ex) { result.Error = ex.Message; }
            result.FinishedUtc = DateTime.UtcNow;
            return result;
        }
    }
}
