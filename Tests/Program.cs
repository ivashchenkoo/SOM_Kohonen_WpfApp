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
            Width = 4, Height = 3, Iterations = 8, Radius = 2, LearningRate = 0.3, Seeds = new[] { 11, 29 }, K = 4, KMeansSeed = 123,
            RowOrder = TrainingRowOrder.SeededShuffle };
        VerifyWizardParity(rows);
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
        Check(result.TrainingTimes.Count == 4 && result.Runs.All(r => result.TrainingSeconds(r.Seed, r.Lattice) > 0),
            "each seed and lattice has an independently measured SOM duration");
        var timingTable = ComparisonTables.BySeed(result);
        foreach (DataRow row in timingTable.Rows)
        {
            int seed = int.Parse((string)row["Seed"]);
            Check((string)row["Навчання квадратної, с"] == ComparisonTables.Number(result.TrainingSeconds(seed, NodeType.Square))
                && (string)row["Навчання гексагональної, с"] == ComparisonTables.Number(result.TrainingSeconds(seed, NodeType.Hexagonal)),
                "timing columns match their seed and topology");
        }
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
            Check(stopped.TrainingTimes.Count == (stage == "Навчання SOM" ? 0 : 1), "keep time only for completed SOM training: " + stage);
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
        var reopened = VerifyResultFile(folder, result, partial);
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
        Check(((System.Windows.Controls.ComboBox)window.FindName("RowOrder")).SelectedIndex == (int)TrainingRowOrder.Source,
            "new comparison uses the Wizard row order");
        VerifyMapsView(window, reopened, partial);
        Check(window.FindName("SeedPreview") is System.Windows.Controls.TextBox, "comparison screen loads");
        var mode = (System.Windows.Controls.ComboBox)window.FindName("SeedMode"); mode.SelectedIndex = 1;
        ((System.Windows.Controls.TextBox)window.FindName("CustomSeeds")).Text = "11, 11";
        Check(((System.Windows.Controls.TextBlock)window.FindName("SeedError")).Text.Contains("11"), "UI duplicate seed error");
        ((System.Windows.Controls.TextBox)window.FindName("CustomSeeds")).Text = "11, 29";
        Check(((System.Windows.Controls.TextBlock)window.FindName("TrainingCount")).Text.Contains("4 навчань"), "UI four-training preview");
        VerifyRowOrderSwitch(window, reopened);
        new SOMWizardWindow().Close(); window.Close();
    }

    private static void VerifyRowOrderSwitch(ComparisonWindow window, ComparisonResult saved)
    {
        // Supply the saved dataset at the file-dialog boundary, then run both series through the same window.
        typeof(ComparisonWindow).GetMethod("RestoreSettings", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(window, new object[] { saved });
        var mode = (System.Windows.Controls.ComboBox)window.FindName("RowOrder");
        var start = (System.Windows.Controls.Button)window.FindName("StartButton");
        var view = (ComparisonMapsView)window.FindName("MapsView");
        var notice = (System.Windows.Controls.TextBlock)window.FindName("PendingRowOrder");
        var applied = (System.Windows.Controls.TextBlock)view.FindName("AppliedOrder");
        ((System.Windows.Controls.ComboBox)window.FindName("SeedMode")).SelectedIndex = 1;
        ((System.Windows.Controls.TextBox)window.FindName("CustomSeeds")).Text = "56391";
        ((System.Windows.Controls.ComboBox)window.FindName("Normalization")).SelectedIndex = 0;
        var completed = new List<ComparisonRun>();
        foreach (var order in new[] { TrainingRowOrder.Source, TrainingRowOrder.SeededShuffle, TrainingRowOrder.Source })
        {
            mode.SelectedIndex = (int)order;
            if (completed.Count > 0)
                Check(notice.Visibility == System.Windows.Visibility.Visible, "unapplied mode change is marked");
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            timer.Tick += (s,e) => { if (start.IsEnabled || elapsed.Elapsed.TotalSeconds > 30) frame.Continue = false; };
            window.Dispatcher.InvokeAsync(() => start.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            timer.Stop();
            Check(start.IsEnabled && notice.Visibility == System.Windows.Visibility.Collapsed, "new run completed and applied the order");
            ((System.Windows.Controls.RadioButton)view.FindName("FeatureMode")).IsChecked = true;
            var displayed = ((System.Windows.Controls.ListBox)view.FindName("FeatureRows")).Items.Cast<SeedFeatureMaps>().Single();
            var tile = displayed.Square.Features.First();
            Check(tile.Result.Config.RowOrder == order && tile.Seed == 56391, "gallery uses the series started from the combo box");
            var expected = order == TrainingRowOrder.Source ? SomTrainer.SourceOrder(saved.Data.Ids.Length) :
                ComparisonExperiment.CreateOrder(saved.Data.Ids.Length, 56391);
            Check(tile.Square.FirstSourceRows.SequenceEqual(expected.Take(5).Select(i => i + 1)) &&
                tile.Square.FirstSourceRows.SequenceEqual(tile.Hex.FirstSourceRows), "both maps report the actual first training rows");
            Check(applied.Text.Contains(string.Join(", ", tile.Square.FirstSourceRows)) &&
                applied.Text.Contains(ComparisonTables.OrderName(order)), "visible order identifies this result");
            var square = (System.Windows.Controls.Canvas)view.FindName("SquareCanvas");
            var cell = square.Children.OfType<System.Windows.Shapes.Polygon>().First();
            cell.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent });
            var means = (System.Windows.Controls.DataGrid)view.FindName("MeansGrid");
            Check(means.Items.Cast<FeatureAverage>().Select(m => m.NodeMean.Value).SequenceEqual(tile.Square.NodeWeights[0]),
                "visible selection table uses this run's neuron weights");
            completed.Add(tile.Square);
        }
        Check(completed[0].InitialWeightsHash == completed[1].InitialWeightsHash &&
            completed[0].OrderHash != completed[1].OrderHash &&
            !completed[0].NodeWeights[0].SequenceEqual(completed[1].NodeWeights[0]), "switch changes the displayed neuron with fixed initialization");
        Check(completed[0].OrderHash == completed[2].OrderHash && completed[0].NodeWeights.SelectMany(w => w)
            .SequenceEqual(completed[2].NodeWeights.SelectMany(w => w)), "switching back exactly reproduces the first map");
    }

    private static void VerifyWizardParity(List<Dictionary<string, object>> rows)
    {
        const int seed = 56391;
        var config = new ComparisonConfig { Dataset = "wizard-parity", IdColumn = "id",
            Features = new List<string> { "calls", "traffic", "constant" }, Normalization = NormalizationMode.None,
            Width = 15, Height = 10, Radius = 7, Iterations = 3, LearningRate = 0.03, Seeds = new[] { seed } };
        var prepared = PreparedComparisonData.Create(rows, config);
        var comparison = ComparisonExperiment.Run(config, prepared, CancellationToken.None);
        Check(comparison.Runs.Count == 2, "both Wizard-compatible maps completed: " + comparison.Error);
        var raw = rows.Select(row => new DataCollection(config.Features.ToDictionary(f => f, f => (object)Convert.ToDouble(row[f])))).ToList();
        foreach (var run in comparison.Runs)
        {
            var wizard = new Map(config.Width, config.Height, seed, run.Lattice);
            wizard.Initialize(raw, config.Features);
            Check(run.InitialWeightsHash == ComparisonExperiment.WeightsHash(wizard), "same initial weights as Wizard");
            SomTrainer.Train(wizard, raw, Enumerable.Range(0, raw.Count).ToArray(), config.Iterations,
                config.LearningRate, config.Radius, CancellationToken.None);
            for (int x = 0; x < wizard.Width; x++)
                for (int y = 0; y < wizard.Height; y++)
                    Check(wizard[x, y].Weights.Select(w => w.GetDoubleValue()).SequenceEqual(run.NodeWeights[x * wizard.Height + y]),
                        "same final weights at each coordinate as Wizard");
            Check(raw.Select(wizard.GetBestMatchingNode).Select(n => (n.X, n.Y))
                .SequenceEqual(run.Assignments.Select(a => (a.BmuX, a.BmuY))), "same BMU for every subscriber as Wizard");
            run.SelectedNodes.Add(13);
            var means = new MapSelection(comparison, seed).Averages();
            Check(means.Select(m => m.NodeMean.Value).SequenceEqual(wizard[1, 3].Weights.Select(w => w.GetDoubleValue())),
                "the selected neuron table uses the same weights as MainWindow");
            run.SelectedNodes.Clear();
        }
    }

    private static ComparisonResult VerifyResultFile(string folder, ComparisonResult result, ComparisonResult partial)
    {
        int seed = result.Config.Seeds[1];
        var pair = result.Runs.Where(r => r.Seed == seed).ToArray();
        var config = result.Config;
        var data = result.Data;
        string id = data.Ids[0];
        var ids = new HashSet<string>();
        var selectedWeights = new List<double[]>();
        foreach (var run in pair)
        {
            var a = run.Assignments.Single(s => s.Id == id);
            int index = a.BmuX * config.Height + a.BmuY;
            run.SelectedNodes.Add(index);
            selectedWeights.Add(run.NodeWeights[index]);
            ids.UnionWith(run.Assignments.Where(s => s.BmuX == a.BmuX && s.BmuY == a.BmuY).Select(s => s.Id));
        }
        result.ViewedSeed = seed;
        var path = Path.Combine(folder, "saved.somcmp");
        ComparisonResultFile.Save(path, partial);
        var incomplete = ComparisonResultFile.Open(path);
        Check(incomplete.Cancelled && incomplete.Runs.Count == 1 && incomplete.Aris.Count == 0, "partial series reopens");
        Check(incomplete.Runs[0].NodeWeights.Length == config.Width * config.Height, "partial series retains complete map");
        ComparisonResultFile.Save(path, result);
        var loaded = ComparisonResultFile.Open(path);
        Check(loaded.ViewedSeed == seed && loaded.Runs.Count == 4 && loaded.CompletedTrainings == 4, "replace and reopen entire series");
        Check(result.Runs.All(r => loaded.TrainingSeconds(r.Seed, r.Lattice) == result.TrainingSeconds(r.Seed, r.Lattice)),
            "timings survive saving and opening with full precision");
        CheckLegacyTiming(path, folder);
        Check(loaded.Data.Ids.SequenceEqual(data.Ids) && loaded.Data.Fingerprint == data.Fingerprint, "subscriber identity survives round trip");
        Check(loaded.Aris.Select(a => a.Value).SequenceEqual(result.Aris.Select(a => a.Value)), "ARIs survive round trip");
        for (int i = 0; i < loaded.Runs.Count; i++)
        {
            var before = result.Runs[i];
            var after = loaded.Runs[i];
            Check(after.NodeClusters.SequenceEqual(before.NodeClusters) && after.NodeWeights.SelectMany(w => w)
                .SequenceEqual(before.NodeWeights.SelectMany(w => w)), "all neuron labels and weights survive round trip");
            Check(after.SelectedNodes.SetEquals(before.SelectedNodes), "per-run selection survives round trip");
            Check(after.Assignments.Select(a => new { a.Id, a.BmuX, a.BmuY, a.Cluster })
                .SequenceEqual(before.Assignments.Select(a => new { a.Id, a.BmuX, a.BmuY, a.Cluster })), "assignments survive round trip");
        }
        var selection = new MapSelection(loaded, seed);
        var means = selection.Averages();
        Check(selection.SubscriberCount == ids.Count && selection.NodeCount == 2, "selection deduplicates by subscriber ID across two lattices");
        for (int j = 0; j < config.Features.Count; j++)
        {
            Near(means[j].SubscriberMean.Value, Enumerable.Range(0, data.Ids.Length)
                .Where(i => ids.Contains(data.Ids[i])).Average(i => data.Original[i][j]), "unique subscriber mean in source units");
            Near(means[j].NodeMean.Value, selectedWeights.Average(w => w[j] * data.Scales[j] + data.Offsets[j]), "inverse-normalized neuron mean");
        }
        var empty = new MapSelection(loaded, result.Config.Seeds[0]);
        Check(empty.Averages().All(m => m.NodeMean == null && m.SubscriberMean == null), "no selection has no averages");
        VerifyCsv(Path.Combine(folder, "summary.csv"), ComparisonTables.Summary(loaded));
        var bad = Path.Combine(folder, "invalid.somcmp");
        File.WriteAllText(bad, "{}");
        try { ComparisonResultFile.Open(bad); }
        catch (InvalidDataException) { return loaded; }
        throw new Exception("Invalid result accepted");
    }

    private static void CheckLegacyTiming(string path, string folder)
    {
        Newtonsoft.Json.Linq.JObject document;
        using (var input = File.OpenRead(path))
        using (var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress))
        using (var reader = new StreamReader(gzip))
            document = Newtonsoft.Json.Linq.JObject.Parse(reader.ReadToEnd());
        ((Newtonsoft.Json.Linq.JObject)document["Result"]).Remove("TrainingTimes");
        ((Newtonsoft.Json.Linq.JObject)document["Result"]["Config"]).Remove("RowOrder");
        var legacyPath = Path.Combine(folder, "legacy-no-times.somcmp");
        using (var file = File.Create(legacyPath))
        using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Compress))
        using (var writer = new StreamWriter(gzip))
            writer.Write(document.ToString());
        var legacy = ComparisonResultFile.Open(legacyPath);
        var run = legacy.Runs.First();
        Check(legacy.Config.RowOrder == TrainingRowOrder.SeededShuffle, "older series restore their original shuffle");
        var replay = ComparisonExperiment.Run(legacy.Config, legacy.Data, CancellationToken.None);
        Check(replay.Runs.Count == legacy.Runs.Count && replay.Runs.Zip(legacy.Runs, (a,b) =>
            a.OrderHash == b.OrderHash && a.NodeWeights.SelectMany(w => w).SequenceEqual(b.NodeWeights.SelectMany(w => w))).All(equal => equal),
            "replaying an older result reproduces the recorded orders and weights");
        Check(!legacy.TrainingSeconds(run.Seed, run.Lattice).HasValue &&
            ComparisonTables.MapHeading(legacy, run.Seed, run.Lattice).Contains("н/д"), "old results open with unavailable training times");
    }

    private static void VerifyMapsView(ComparisonWindow window, ComparisonResult loaded, ComparisonResult partial)
    {
        var view = (ComparisonMapsView)window.FindName("MapsView");
        view.Display(loaded);
        Check((string)((System.Windows.Controls.GroupBox)view.FindName("SquareGroup")).Header ==
            ComparisonTables.MapHeading(loaded, loaded.ViewedSeed.Value, NodeType.Square), "cluster heading uses the current run's time");
        var square = (System.Windows.Controls.Canvas)view.FindName("SquareCanvas");
        var hex = (System.Windows.Controls.Canvas)view.FindName("HexCanvas");
        var grid = (System.Windows.Controls.DataGrid)view.FindName("MeansGrid");
        var sqCells = square.Children.OfType<System.Windows.Shapes.Polygon>().ToArray();
        var hexCells = hex.Children.OfType<System.Windows.Shapes.Polygon>().ToArray();
        Check(sqCells.Length == 12 && sqCells.All(p => p.Points.Count == 4), "square map displays all neurons");
        Check(hexCells.Length == 12 && hexCells.All(p => p.Points.Count == 6), "hexagonal map displays all neurons");
        var clear = (System.Windows.Controls.Button)view.FindName("ClearButton");
        clear.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(grid.Items.Cast<FeatureAverage>().All(m => m.SubscriberMean == null), "clear selection updates table");
        var shape = sqCells[0];
        var mouse = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = System.Windows.Input.Mouse.MouseDownEvent };
        shape.RaiseEvent(mouse);
        Check(grid.Items.Cast<FeatureAverage>().All(m => m.NodeMean.HasValue), "click updates average weights");
        Check(clear.IsEnabled && shape.StrokeThickness > 1, "selected node has visible outline");
        VerifyFeatureMaps(view, loaded);
        view.Display(partial);
        Check(square.Children.OfType<System.Windows.Shapes.Polygon>().Count() == 12 && !hex.Children.OfType<System.Windows.Shapes.Polygon>().Any(),
            "cancelled series renders only completed lattice");
        ((System.Windows.Controls.RadioButton)view.FindName("FeatureMode")).IsChecked = true;
        var incomplete = ((System.Windows.Controls.ListBox)view.FindName("FeatureRows")).Items.Cast<SeedFeatureMaps>().First();
        Check(incomplete.Square.Available && !incomplete.Hex.Available, "partial feature map keeps missing hex side empty");
    }

    private static void VerifyFeatureMaps(ComparisonMapsView view, ComparisonResult loaded)
    {
        var mode = (System.Windows.Controls.RadioButton)view.FindName("FeatureMode");
        var rows = (System.Windows.Controls.ListBox)view.FindName("FeatureRows");
        var menu = (System.Windows.Controls.MenuItem)view.FindName("FeatureMenu");
        mode.IsChecked = true;
        var group = rows.Items.Cast<SeedFeatureMaps>().Single();
        Check(group.Hex.Heading == ComparisonTables.MapHeading(loaded, group.Seed, NodeType.Hexagonal) &&
            group.Square.Heading == ComparisonTables.MapHeading(loaded, group.Seed, NodeType.Square) &&
            !group.Hex.Heading.Contains("н/д"), "feature gallery and PNG template have both measured timings");
        Check(group.Hex.Features.Count == loaded.Config.Features.Count && menu.Items.Count == group.Square.Features.Count,
            "both lattice groups contain the selected seed's features");
        var first = group.Hex.Features[0];
        Check(first.Seed == loaded.ViewedSeed && first.Square.Seed == first.Hex.Seed, "feature pairs use the selected seed");
        var weights = loaded.Runs.SelectMany(r => r.NodeWeights).Select(w => w[0]).ToArray();
        Near(first.Minimum, weights.Min(), "global per-feature lower bound");
        Near(first.Maximum, weights.Max(), "global per-feature upper bound");
        Near(first.Intensity(first.Minimum), 0, "minimum heat color");
        Near(first.Intensity(first.Maximum), 1, "maximum heat color");
        var constant = group.Hex.Features[2];
        Check(constant.Constant && constant.Intensity(constant.Minimum) == 0.5 && constant.LowLabel == "5", "constant feature legend in original units");
        var toggle = (System.Windows.Controls.MenuItem)menu.Items[2];
        toggle.IsChecked = false;
        toggle.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        var all = (System.Windows.Controls.CheckBox)view.FindName("AllSeeds");
        all.IsChecked = true;
        all.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Check(rows.Items.Cast<SeedFeatureMaps>().Select(p => p.Seed).SequenceEqual(new[] { 11, 29 }),
            "one section per seed in experiment order");
        Check(rows.Items.Cast<SeedFeatureMaps>().All(g => g.Hex.Features.All(p => p.Name != constant.Name)
            && g.Square.Features.All(p => p.Name != constant.Name)), "hidden feature omitted from all groups");
        view.Measure(new System.Windows.Size(900, 620));
        view.Arrange(new System.Windows.Rect(0, 0, 900, 620));
        view.UpdateLayout();
        var container = (System.Windows.Controls.ListBoxItem)rows.ItemContainerGenerator.ContainerFromIndex(0);
        var planes = FindPlanes(container).OrderBy(p => p.TranslatePoint(new System.Windows.Point(), view).X).ToArray();
        Check(planes.Select(p => p.Lattice).SequenceEqual(new[] { NodeType.Hexagonal, NodeType.Hexagonal, NodeType.Square, NodeType.Square }),
            "left and right blocks each show adjacent feature maps of the same topology");
        Check(planes.Select(p => ((FeatureMapPair)p.DataContext).Feature).SequenceEqual(new[] { 0, 1, 0, 1 })
            && planes.All(p => ((FeatureMapPair)p.DataContext).Seed == 11), "visible tiles grouped by seed, then topology, then feature");
        Check(planes.Select(p => p.TranslatePoint(new System.Windows.Point(), view).Y).Distinct().Count() == 1,
            "feature tiles in each block share a horizontal row when space permits");
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-export");
        string png = Path.Combine(folder, "feature-maps.png");
        view.WriteFeaturePng(png);
        using (var input = File.OpenRead(png))
        {
            var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(input,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = frame.PixelWidth * 4;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            var colored = new int[2,4];
            for (int i = 0; i < pixels.Length; i += 4)
                if (Math.Abs(pixels[i] - pixels[i + 2]) > 100)
                    colored[(i / stride) * 2 / frame.PixelHeight, (i % stride) * 4 / stride]++;
            Check(frame.PixelWidth == 1350 && colored.Cast<int>().All(count => count > 4000),
                "PNG shows four feature planes across for each of the two seeds");
        }
        string saved = Path.Combine(folder, "feature-view.somcmp");
        ComparisonResultFile.Save(saved, loaded);
        var reopened = ComparisonResultFile.Open(saved);
        view.Display(reopened);
        Check(mode.IsChecked == true && all.IsChecked == true && rows.Items.Count == 2 && reopened.HiddenMapFeatures.Contains(constant.Name),
            "feature visibility, scope and view mode survive reopening");
        foreach (System.Windows.Controls.MenuItem item in menu.Items)
        {
            item.IsChecked = false;
            item.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        }
        Check(rows.Items.Count == 0 && !((System.Windows.Controls.Button)view.FindName("SavePng")).IsEnabled, "all features can be hidden");
    }
    private static IEnumerable<FeaturePlane> FindPlanes(System.Windows.DependencyObject root)
    {
        if (root is FeaturePlane plane) yield return plane;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindPlanes(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
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
