using System;
using System.Collections.Generic;
using System.Linq;

namespace SOM_Kohonen_WpfApp.Service
{
    public sealed class FeatureAverage
    {
        public string Feature { get; set; }
        public double? NodeMean { get; set; }
        public double? SubscriberMean { get; set; }
    }

    // Both lattices share source row indices. One subscriber selected on both maps counts once.
    public sealed class MapSelection
    {
        private readonly ComparisonResult result;
        private readonly Dictionary<ComparisonRun, ILookup<int, int>> members =
            new Dictionary<ComparisonRun, ILookup<int, int>>();
        public int NodeCount { get; private set; }
        public int SubscriberCount { get; private set; }

        public MapSelection(ComparisonResult result, int seed)
        {
            this.result = result;
            var sourceRows = result.Data.Ids.Select((id, row) => new { id, row })
                .ToDictionary(item => item.id, item => item.row, StringComparer.Ordinal);
            foreach (var run in result.Runs.Where(r => r.Seed == seed))
                members.Add(run, run.Assignments.ToLookup(a => a.BmuX * result.Config.Height + a.BmuY, a => sourceRows[a.Id]));
        }

        public int Hits(ComparisonRun run, int node) => members[run][node].Count();

        public List<FeatureAverage> Averages()
        {
            var selectedRows = new HashSet<int>();
            var weights = new List<double[]>();
            foreach (var pair in members)
            {
                foreach (int node in pair.Key.SelectedNodes)
                {
                    weights.Add(pair.Key.NodeWeights[node]);
                    selectedRows.UnionWith(pair.Value[node]);
                }
            }
            NodeCount = weights.Count;
            SubscriberCount = selectedRows.Count;
            var data = result.Data;
            return result.Config.Features.Select((name, j) => new FeatureAverage
            {
                Feature = name,
                NodeMean = NodeCount == 0 ? (double?)null : weights.Average(w => w[j]) * data.Scales[j] + data.Offsets[j],
                SubscriberMean = SubscriberCount == 0 ? (double?)null : selectedRows.Average(i => data.Original[i][j])
            }).ToList();
        }
    }
}
