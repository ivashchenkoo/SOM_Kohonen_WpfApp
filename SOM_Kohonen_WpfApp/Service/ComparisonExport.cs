using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Service
{
    // UI and CSV consume exactly the same tables and round-trip numeric strings.
    public static class ComparisonTables
    {
        public static string Number(double? value) => value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "н/д";
        public static string Lattice(NodeType value) => value == NodeType.Square ? "Квадратна" : "Гексагональна";
        private static DataTable Table(params string[] columns)
        {
            var table = new DataTable();
            foreach (var column in columns) table.Columns.Add(column, typeof(string));
            return table;
        }
        public static DataTable BySeed(ComparisonResult result)
        {
            var table = Table("Seed", "Силует квадратної", "Силует гексагональної", "Різниця (гекс − кв)", "ARI", "Статус", "Пояснення");
            foreach (int seed in result.Config.Seeds)
            {
                var square = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Square);
                var hex = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Hexagonal);
                var ari = result.Aris.FirstOrDefault(a => a.SeedA == seed && a.SeedB == seed && a.LatticeA != a.LatticeB);
                table.Rows.Add(seed, Number(square?.Silhouette.Value), Number(hex?.Silhouette.Value),
                    Number(hex?.Silhouette.Value - square?.Silhouette.Value), Number(ari?.Value),
                    square != null && hex != null ? "Завершена" : square != null || hex != null ? "Неповна пара" : "Не завершена",
                    string.Join("; ", new[] { square?.Silhouette.Reason, hex?.Silhouette.Reason }.Where(s => s != null).Distinct()));
            }
            return table;
        }
        public static DataTable Summary(ComparisonResult result)
        {
            var table = Table("Показник", "Кількість", "Медіана", "Мінімум", "Максимум", "Пояснення");
            table.Rows.Add("Завершені запуски з показниками", result.Runs.Count);
            table.Rows.Add("Завершені навчання SOM", result.CompletedTrainings);
            table.Rows.Add("Повні пари", result.Config.Seeds.Count(s => result.Runs.Count(r => r.Seed == s) == 2));
            foreach (var lattice in new[] { NodeType.Square, NodeType.Hexagonal })
            {
                var runs = result.Runs.Where(r => r.Lattice == lattice).ToArray();
                table.Rows.Add("Завершені запуски: " + Lattice(lattice), runs.Length);
                AddSummary(table, "Силует: " + Lattice(lattice), runs.Select(r => r.Silhouette.Value),
                    string.Join("; ", runs.Select(r => r.Silhouette.Reason).Where(s => s != null).Distinct()));
                AddSummary(table, "Стабільність ARI: " + Lattice(lattice), result.Aris.Where(a => a.LatticeA == lattice && a.LatticeB == lattice).Select(a => (double?)a.Value),
                    "Потрібно щонайменше два завершені запуски цієї решітки.");
            }
            AddSummary(table, "Парна різниця силуету (гекс − кв)", result.Config.Seeds.Select(seed =>
            {
                var s = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Square);
                var h = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Hexagonal);
                return h?.Silhouette.Value - s?.Silhouette.Value;
            }), "Немає повних пар із визначеним силуетом обох решіток.");
            AddSummary(table, "ARI між решітками", result.Aris.Where(a => a.LatticeA != a.LatticeB).Select(a => (double?)a.Value), "Немає повних пар.");
            return table;
        }
        private static void AddSummary(DataTable table, string name, IEnumerable<double?> source, string reason)
        {
            var values = source.Where(v => v.HasValue).Select(v => v.Value).OrderBy(v => v).ToArray();
            if (values.Length == 0) table.Rows.Add(name, 0, "н/д", "н/д", "н/д", string.IsNullOrEmpty(reason) ? "Немає завершених запусків." : reason);
            else table.Rows.Add(name, values.Length, Number((values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2), Number(values.First()), Number(values.Last()));
        }
        public static DataTable Aris(ComparisonResult result)
        {
            var table = Table("Seed A", "Решітка A", "Seed B", "Решітка B", "ARI");
            foreach (var a in result.Aris) table.Rows.Add(a.SeedA, Lattice(a.LatticeA), a.SeedB, Lattice(a.LatticeB), Number(a.Value));
            return table;
        }
        public static DataTable Profiles(ComparisonResult result)
        {
            var table = Table(new[] { "Seed", "Решітка", "Кластер", "Кількість", "Частка" }.Concat(result.Config.Features.Select(f => "Середнє: " + f)).ToArray());
            foreach (var run in result.Runs) foreach (var p in run.Profiles)
                table.Rows.Add(new object[] { run.Seed, Lattice(run.Lattice), p.Cluster, p.Count, Number(p.Share) }.Concat(p.Means.Select(m => (object)Number(m))).ToArray());
            return table;
        }
        public static DataTable MatchedProfiles(ComparisonResult result, int seed)
        {
            var table = Table(new[] { "Seed", "Кластер кв", "Кластер гекс", "Кількість кв", "Частка кв", "Кількість гекс", "Частка гекс", "Спільні абоненти" }
                .Concat(result.Config.Features.SelectMany(f => new[] { "Кв: " + f, "Гекс: " + f })).ToArray());
            var square = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Square);
            var hex = result.Runs.FirstOrDefault(r => r.Seed == seed && r.Lattice == NodeType.Hexagonal);
            if (square == null || hex == null) return table;
            var right = hex.Assignments.ToDictionary(a => a.Id, a => a.Cluster, StringComparer.Ordinal);
            var overlaps = new int[result.Config.K, result.Config.K];
            foreach (var a in square.Assignments) overlaps[a.Cluster, right[a.Id]]++;
            var matching = ComparisonMetrics.MatchClusters(overlaps);
            for (int i = 0; i < matching.Length; i++)
            {
                var a = square.Profiles[i]; var b = hex.Profiles[matching[i]];
                table.Rows.Add(new object[] { seed, a.Cluster, b.Cluster, a.Count, Number(a.Share), b.Count, Number(b.Share), overlaps[i, matching[i]] }
                    .Concat(a.Means.SelectMany((m, j) => new object[] { Number(m), Number(b.Means[j]) })).ToArray());
            }
            return table;
        }
    }

    public static class CsvExporter
    {
        public static string Escape(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        public static void Write(string path, DataTable table)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName))));
                foreach (DataRow row in table.Rows) writer.WriteLine(string.Join(",", row.ItemArray.Select(Escape)));
            }
        }
    }

    public static class ComparisonExport
    {
        public static void Write(string folder, ComparisonResult result, bool assignmentsOnly = false)
        {
            Directory.CreateDirectory(folder);
            if (assignmentsOnly)
            {
                // Stream detailed rows; avoid a second N * runs in-memory table.
                using (var writer = new StreamWriter(Path.Combine(folder, "assignments.csv"), false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("seed,lattice,subscriber_id,bmu_x,bmu_y,cluster");
                    foreach (var run in result.Runs) foreach (var a in run.Assignments)
                        writer.WriteLine(string.Join(",", new object[] { run.Seed, run.Lattice, a.Id, a.BmuX, a.BmuY, a.Cluster }.Select(CsvExporter.Escape)));
                }
            }
            else
            {
                CsvExporter.Write(Path.Combine(folder, "summary.csv"), ComparisonTables.Summary(result));
                CsvExporter.Write(Path.Combine(folder, "metrics_by_seed.csv"), ComparisonTables.BySeed(result));
                CsvExporter.Write(Path.Combine(folder, "pairwise_ari.csv"), ComparisonTables.Aris(result));
                CsvExporter.Write(Path.Combine(folder, "cluster_profiles.csv"), ComparisonTables.Profiles(result));
                DataTable matched = ComparisonTables.MatchedProfiles(result, result.Config.Seeds[0]);
                foreach (int seed in result.Config.Seeds.Skip(1)) matched.Merge(ComparisonTables.MatchedProfiles(result, seed));
                CsvExporter.Write(Path.Combine(folder, "matched_profiles.csv"), matched);
            }
            JsonIOService.WriteObjectToJson(Path.Combine(folder, "experiment.json"), new
            {
                result.Config, result.StartedUtc, result.FinishedUtc, result.Cancelled, result.Error, result.CompletedTrainings,
                RowCount = result.Data.Ids.Length, DataSha256 = result.Data.Fingerprint,
                result.Data.Offsets, result.Data.Scales,
                IdPolicy = result.Config.IdColumn ?? "row:<1-based source row index>",
                Runs = result.Runs.Select(r => new { r.Seed, r.Lattice, r.InitialWeightsHash, r.OrderHash }),
                Silhouette = "Exact Euclidean, all subscribers; singleton=0; undefined unless 2 <= occupied clusters <= N-1",
                ProfileMeans = "Original unnormalized values; empty clusters have missing means",
                Matching = "Hungarian maximum total subscriber intersection; zero-based cluster IDs and BMU coordinates"
            });
        }
    }
}
