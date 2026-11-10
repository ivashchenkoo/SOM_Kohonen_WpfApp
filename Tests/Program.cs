using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SOM_Kohonen_WpfApp.Models;
using SOM_Kohonen_WpfApp.Service;
using SOM_Kohonen_WpfApp.SOM;
using SOM_Kohonen_WpfApp.Views;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < 1e-12, message);
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { assertions++; return; }
        throw new Exception(message);
    }
    private sealed class ImmediateProgress : IProgress<ExperimentProgress>
    {
        private readonly Action<ExperimentProgress> action;
        public ImmediateProgress(Action<ExperimentProgress> action) { this.action = action; }
        public void Report(ExperimentProgress value) => action(value);
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            Run(); Console.WriteLine("PASS: " + assertions + " assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run()
    {
        var rows = Enumerable.Range(0, 24).Select(i => new Dictionary<string, object>
        {
            ["id"] = "абонент,\"" + i, ["calls"] = (i / 6) * 15.0 + i % 6, ["traffic"] = (i / 6) * 20.0 + (i % 3) * 2,
            ["constant"] = 5.0
        }).ToList();
        var config = new ComparisonConfig { Dataset = "synthetic-24", IdColumn = "id", Features = new List<string> { "calls", "traffic", "constant" },
            Width = 4, Height = 3, Iterations = 8, Radius = 2, LearningRate = 0.3, Seeds = new[] { 11, 29 }, K = 4, KMeansSeed = 123 };
        var data = PreparedComparisonData.Create(rows, config);
        Check(data.Normalized.All(r => r[2].GetDoubleValue() == 0), "constant normalization");
        Check(data.Original[0][2] == 5, "original data retained");
        var initial = new Map(4, 3, 11); initial.Initialize(data.Normalized, config.Features);
        var sq = ComparisonExperiment.CopyMap(initial, NodeType.Square);
        var hx = ComparisonExperiment.CopyMap(initial, NodeType.Hexagonal);
        Check(ComparisonExperiment.WeightsHash(sq) == ComparisonExperiment.WeightsHash(hx), "equal initial weights");
        sq[0, 0].Weights[0].Value = 999.0;
        Check(hx[0, 0].Weights[0].GetDoubleValue() != 999, "deep copies");
        Check(ComparisonExperiment.CreateOrder(24, 11).SequenceEqual(ComparisonExperiment.CreateOrder(24, 11)), "identical order");
        Check(!ComparisonExperiment.CreateOrder(24, 11).SequenceEqual(ComparisonExperiment.CreateOrder(24, 29)), "distinct order seeds");
        Check(KMeansClustering.Cluster(new[] { new[] { 1.0 } }, 4).Length == 1,
            "legacy clustering still permits more clusters than neurons");
        var events = new List<ExperimentProgress>();
        var result = ComparisonExperiment.Run(config, data, CancellationToken.None, new ImmediateProgress(events.Add));
        Check(result.Error == null && !result.Cancelled, "completed series: " + result.Error);
        Check(result.Runs.Count == 4 && result.CompletedTrainings == 4, "exactly four trainings");
        Check(events.Count(e => e.Stage == "Навчання SOM" && e.Iteration == config.Iterations) == 4, "four complete iteration sequences");
        Check(result.Aris.Count == 4, "two paired ARIs and two stability ARIs");
        foreach (int seed in config.Seeds)
        {
            var pair = result.Runs.Where(r => r.Seed == seed).ToArray();
            Check(pair[0].InitialWeightsHash == pair[1].InitialWeightsHash && pair[0].OrderHash == pair[1].OrderHash, "paired initial conditions");
        }
        foreach (var run in result.Runs)
        {
            Check(run.Assignments.Count == rows.Count && run.Assignments.Select(a => a.Id).Distinct().Count() == rows.Count, "one assignment per subscriber");
            Check(run.Assignments.All(a => a.Cluster >= 0 && a.Cluster < 4 && a.BmuX >= 0 && a.BmuX < 4 && a.BmuY >= 0 && a.BmuY < 3), "valid labels and BMUs");
            Check(run.Profiles.Count == config.K && run.Profiles.Sum(p => p.Count) == rows.Count, "profile counts");
            Near(run.Profiles.Sum(p => p.Share), 1, "profile shares");
            foreach (var p in run.Profiles)
            {
                var indices = run.Assignments.Select((a, i) => new { a, i }).Where(x => x.a.Cluster == p.Cluster).Select(x => x.i).ToArray();
                if (indices.Length == 0) Check(p.Means.All(m => !m.HasValue), "empty means");
                else for (int j = 0; j < config.Features.Count; j++) Near(p.Means[j].Value, indices.Average(i => data.Original[i][j]), "raw means");
            }
        }
        var repeat = ComparisonExperiment.Run(config, data, CancellationToken.None);
        for (int i = 0; i < 4; i++)
        {
            Check(result.Runs[i].Assignments.Select(a => $"{a.Id}:{a.BmuX}:{a.BmuY}:{a.Cluster}")
                .SequenceEqual(repeat.Runs[i].Assignments.Select(a => $"{a.Id}:{a.BmuX}:{a.BmuY}:{a.Cluster}")), "reproducible assignments");
            Check(result.Runs[i].Silhouette.Value == repeat.Runs[i].Silhouette.Value, "reproducible silhouette");
        }
        var cancel = new CancellationTokenSource();
        var partial = ComparisonExperiment.Run(config, data, cancel.Token, new ImmediateProgress(p => { if (p.Stage == "Запуск завершено") cancel.Cancel(); }));
        Check(partial.Cancelled && partial.Runs.Count == 1 && partial.Aris.Count == 0, "cancellation retains square, excludes pair ARI");
        Check(ComparisonTables.BySeed(partial).Rows[0][5].ToString() == "Неповна пара", "incomplete pair status");
        foreach (string stage in new[] { "Навчання SOM", "k-means", "Розрахунок показників" })
        {
            var source = new CancellationTokenSource();
            var stopped = ComparisonExperiment.Run(config, data, source.Token, new ImmediateProgress(p => { if (p.Stage == stage) source.Cancel(); }));
            Check(stopped.Cancelled && stopped.Runs.Count == 0, "cancel stage " + stage);
        }
        Check(ComparisonExperiment.Run(config, data, CancellationToken.None).Runs.Count == 4, "restart after cancellation");
        Metrics();
        Reject(() => ComparisonConfig.ParseSeeds("11, 11"), "duplicate seeds accepted");
        Check(ComparisonConfig.ParseSeeds("-1; 0\n42").SequenceEqual(new[] { -1, 0, 42 }), "manual seed parsing");
        Check(ComparisonConfig.GenerateSeeds(42, 10).Distinct().Count() == 10, "unique generated seeds");
        Check(ComparisonConfig.GenerateSeeds(42, 10).SequenceEqual(ComparisonConfig.GenerateSeeds(42, 10)), "reproducible generated seeds");
        rows[1]["id"] = rows[0]["id"];
        Reject(() => PreparedComparisonData.Create(rows, config), "duplicate ID accepted");
        config.IdColumn = null;
        var internalIds = PreparedComparisonData.Create(rows, config);
        Check(internalIds.Ids[0] == "row:1" && internalIds.Ids.Distinct().Count() == 24, "source row IDs");
        config.IdColumn = "id"; rows[1]["id"] = "абонент,\"1";
        config.Normalization = NormalizationMode.ZScore;
        var z = PreparedComparisonData.Create(rows, config);
        Near(z.Normalized.Average(r => r[0].GetDoubleValue()), 0, "zscore mean");
        Near(z.Normalized.Average(r => Math.Pow(r[0].GetDoubleValue(), 2)), 1, "zscore variance");
        config.Normalization = NormalizationMode.MinMax;
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-export");
        ComparisonExport.Write(folder, result);
        ComparisonExport.Write(folder, result, true);
        JsonIOService.WriteObjectToJson(Path.Combine(folder, "source.json"), rows);
        var imported = JsonIOService.ReadObjectFromJson<List<Dictionary<string, object>>>(Path.Combine(folder, "source.json"));
        Check(PreparedComparisonData.Create(imported, config).Fingerprint == data.Fingerprint, "JSON import preserves IDs and numeric data");
        VerifyCsv(Path.Combine(folder, "summary.csv"), ComparisonTables.Summary(result));
        VerifyCsv(Path.Combine(folder, "metrics_by_seed.csv"), ComparisonTables.BySeed(result));
        VerifyCsv(Path.Combine(folder, "pairwise_ari.csv"), ComparisonTables.Aris(result));
        VerifyCsv(Path.Combine(folder, "cluster_profiles.csv"), ComparisonTables.Profiles(result));
        var matchingTable = ComparisonTables.MatchedProfiles(result, config.Seeds[0]);
        matchingTable.Merge(ComparisonTables.MatchedProfiles(result, config.Seeds[1]));
        VerifyCsv(Path.Combine(folder, "matched_profiles.csv"), matchingTable);
        ComparisonExport.Write(Path.Combine(folder, "partial"), partial);
        VerifyCsv(Path.Combine(folder, "partial", "metrics_by_seed.csv"), ComparisonTables.BySeed(partial));
        var assignmentRows = ParseCsv(File.ReadAllText(Path.Combine(folder, "assignments.csv")));
        Check(assignmentRows.Count == 97, "CSV has all 96 assignments");
        Check(assignmentRows[1][2] == data.Ids[0], "CSV quoted UTF-8 ID");
        var metadata = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(folder, "experiment.json")));
        Check(metadata["Config"]["Seeds"].Count() == 2 && metadata["DataSha256"].ToString() == data.Fingerprint, "export reproducibility metadata");
        Check(File.ReadAllBytes(Path.Combine(folder, "summary.csv")).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "UTF-8 BOM");
        // Load real WPF resources and construct both screens to catch XAML and handler regressions.
        var app = new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("/SOM_Kohonen_WpfApp;component/Styles/Datagrid.xaml", UriKind.Relative) });
        var window = new ComparisonWindow();
        Check(window.FindName("SeedPreview") is System.Windows.Controls.TextBox, "comparison screen loads");
        var mode = (System.Windows.Controls.ComboBox)window.FindName("SeedMode"); mode.SelectedIndex = 1;
        ((System.Windows.Controls.TextBox)window.FindName("CustomSeeds")).Text = "11, 11";
        Check(((System.Windows.Controls.TextBlock)window.FindName("SeedError")).Text.Contains("11"), "UI duplicate seed error");
        ((System.Windows.Controls.TextBox)window.FindName("CustomSeeds")).Text = "11, 29";
        Check(((System.Windows.Controls.TextBlock)window.FindName("TrainingCount")).Text.Contains("4 навчань"), "UI four-training preview");
        new SOMWizardWindow().Close(); window.Close();
    }
    private static void Metrics()
    {
        var left = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0, ["c"] = 1, ["d"] = 1 };
        var renamed = new Dictionary<string, int> { ["d"] = 8, ["c"] = 8, ["b"] = 7, ["a"] = 7 };
        Near(ComparisonMetrics.Ari(left, renamed), 1, "ARI ID alignment and label permutation");
        Near(ComparisonMetrics.Ari(left, new Dictionary<string, int> { ["a"] = 0, ["b"] = 1, ["c"] = 0, ["d"] = 1 }), -0.5, "ARI negative example");
        Reject(() => ComparisonMetrics.Ari(left, new Dictionary<string, int>()), "ARI mismatched IDs");
        var points = new[] { 0.0, 1.0, 10.0, 11.0 }.Select(v => new DataCollection(new Dictionary<string, object> { ["x"] = v })).ToList();
        Near(ComparisonMetrics.Silhouette(points, new[] { 0, 0, 1, 1 }, CancellationToken.None).Value.Value,
            ((1 - 1 / 10.5) + (1 - 1 / 9.5)) / 2, "exact silhouette");
        Check(!ComparisonMetrics.Silhouette(points, new[] { 0, 0, 0, 0 }, CancellationToken.None).Value.HasValue, "one-cluster silhouette undefined");
        Check(!ComparisonMetrics.Silhouette(points, new[] { 0, 1, 2, 3 }, CancellationToken.None).Value.HasValue, "all-singleton silhouette undefined");
        var overlap = new[,] { { 9, 8, 0 }, { 8, 0, 0 }, { 0, 0, 0 } };
        Check(ComparisonMetrics.MatchClusters(overlap).SequenceEqual(new[] { 1, 0, 2 }), "global maximum matching, not greedy; empty cluster");
        var center = new Node(2, 2, new DataCollection());
        Check(Enumerable.Range(0, 5).Sum(x => Enumerable.Range(0, 5).Count(y => center.DistanceToSquared(new Node(x, y, new DataCollection()), NodeType.Hexagonal) == 1)) == 6, "six hex neighbours");
        Check(Enumerable.Range(0, 5).Sum(x => Enumerable.Range(0, 5).Count(y => center.DistanceToSquared(new Node(x, y, new DataCollection()), NodeType.Square) == 1)) == 4, "four square neighbours");
    }
    private static void VerifyCsv(string path, DataTable table)
    {
        var rows = ParseCsv(File.ReadAllText(path));
        Check(rows.Count == table.Rows.Count + 1, "CSV row count " + path);
        for (int i = 0; i < table.Rows.Count; i++)
            Check(rows[i + 1].SequenceEqual(table.Rows[i].ItemArray.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))), "CSV equals UI row " + i);
    }
    private static List<string[]> ParseCsv(string csv)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new System.Text.StringBuilder(); bool quoted = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"') { if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' && !quoted) { row.Add(field.ToString().TrimEnd('\r')); rows.Add(row.ToArray()); row.Clear(); field.Clear(); }
            else field.Append(c);
        }
        return rows;
    }
}
