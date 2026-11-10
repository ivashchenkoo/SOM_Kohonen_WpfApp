using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SOM_Kohonen_WpfApp.Models;

namespace SOM_Kohonen_WpfApp.Service
{
    public sealed class MetricValue
    {
        public double? Value { get; set; }
        public string Reason { get; set; }
        public static MetricValue Missing(string reason) => new MetricValue { Reason = reason };
    }

    public static class ComparisonMetrics
    {
        public static MetricValue Silhouette(IList<DataCollection> rows, int[] labels, CancellationToken token)
        {
            var groups = labels.Distinct().OrderBy(x => x).ToArray();
            if (groups.Length < 2 || groups.Length >= rows.Count)
                return MetricValue.Missing("Потрібно від 2 до N−1 непорожніх кластерів.");
            var slots = groups.Select((g, i) => new { g, i }).ToDictionary(x => x.g, x => x.i);
            var counts = groups.Select(g => labels.Count(l => l == g)).ToArray();
            double total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                int own = slots[labels[i]];
                if (counts[own] == 1) continue; // Standard singleton silhouette is zero.
                var sums = new double[groups.Length];
                for (int j = 0; j < rows.Count; j++)
                {
                    if ((j & 255) == 0) token.ThrowIfCancellationRequested();
                    if (i != j) sums[slots[labels[j]]] += Math.Sqrt(rows[i].DistanceToSquared(rows[j]));
                }
                double a = sums[own] / (counts[own] - 1);
                double b = Enumerable.Range(0, groups.Length).Where(g => g != own).Min(g => sums[g] / counts[g]);
                if (Math.Max(a, b) > 0) total += (b - a) / Math.Max(a, b);
            }
            return new MetricValue { Value = total / rows.Count };
        }

        public static double Ari(IDictionary<string, int> left, IDictionary<string, int> right)
        {
            if (left.Count != right.Count || left.Keys.Any(id => !right.ContainsKey(id)))
                throw new ArgumentException("ARI потребує однакового набору ID.");
            if (left.Count < 2) return 1;
            var contingency = left.Select(p => new { A = p.Value, B = right[p.Key] }).ToArray();
            double cells = contingency.GroupBy(p => new { p.A, p.B }).Sum(g => Choose2(g.Count()));
            double a = contingency.GroupBy(p => p.A).Sum(g => Choose2(g.Count()));
            double b = contingency.GroupBy(p => p.B).Sum(g => Choose2(g.Count()));
            double expected = a * b / Choose2(left.Count);
            double denominator = (a + b) / 2 - expected;
            return denominator == 0 ? 1 : (cells - expected) / denominator;
        }

        private static double Choose2(int n) => (double)n * (n - 1) / 2;

        // Hungarian assignment, O(k^3). Maximizes total intersection including empty clusters.
        public static int[] MatchClusters(int[,] overlaps)
        {
            int n = overlaps.GetLength(0);
            var u = new double[n + 1]; var v = new double[n + 1];
            var p = new int[n + 1]; var way = new int[n + 1];
            for (int i = 1; i <= n; i++)
            {
                p[0] = i;
                int j0 = 0;
                var min = Enumerable.Repeat(double.PositiveInfinity, n + 1).ToArray();
                var used = new bool[n + 1];
                do
                {
                    used[j0] = true;
                    int i0 = p[j0], j1 = 0;
                    double delta = double.PositiveInfinity;
                    for (int j = 1; j <= n; j++) if (!used[j])
                    {
                        double cur = -overlaps[i0 - 1, j - 1] - u[i0] - v[j];
                        if (cur < min[j]) { min[j] = cur; way[j] = j0; }
                        if (min[j] < delta) { delta = min[j]; j1 = j; }
                    }
                    for (int j = 0; j <= n; j++)
                        if (used[j]) { u[p[j]] += delta; v[j] -= delta; } else min[j] -= delta;
                    j0 = j1;
                } while (p[j0] != 0);
                do { int j1 = way[j0]; p[j0] = p[j1]; j0 = j1; } while (j0 != 0);
            }
            var match = new int[n];
            for (int j = 1; j <= n; j++) match[p[j] - 1] = j - 1;
            return match;
        }
    }
}
