using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using SOM_Kohonen_WpfApp.Service;

namespace SOM_Kohonen_WpfApp.Views
{
    public sealed class ComparisonFeature
    {
        public string Name { get; set; }
        public bool Selected { get; set; }
    }
    public partial class ComparisonWindow : Window
    {
        private List<Dictionary<string, object>> rows;
        private List<ComparisonFeature> features;
        private string dataset;
        private CancellationTokenSource cancellation;
        private ComparisonResult result;
        private bool ready;
        private bool busy;
        public ComparisonWindow()
        {
            InitializeComponent();
            ready = true;
            RefreshSeeds();
        }
        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            SetBusy(true);
            try
            {
                var imported = await Task.Run(() => JsonIOService.ReadObjectFromJson<List<Dictionary<string, object>>>(dialog.FileName));
                if (imported == null || imported.Count < 2 || imported.Any(r => r == null)) throw new ArgumentException("Потрібен JSON-масив щонайменше з двох об’єктів.");
                var columns = imported.SelectMany(r => r.Keys).Distinct().ToList();
                features = columns.Where(c => imported.All(r => { object v; double n; return r.TryGetValue(c, out v) && PreparedComparisonData.TryNumber(v, out n); }))
                    .Select(c => new ComparisonFeature { Name = c, Selected = true }).ToList();
                rows = imported; dataset = dialog.FileName;
                IdColumn.ItemsSource = new[] { "(ID вихідного рядка)" }.Concat(columns).ToArray();
                var autoId = columns.FindIndex(c => new[] { "id", "code", "код", "subscriber_id" }.Contains(c.ToLowerInvariant()));
                IdColumn.SelectedIndex = autoId + 1;
                FeaturesGrid.ItemsSource = features;
                Id_Changed(null, null);
                DatasetText.Text = Path.GetFileName(dataset) + " — " + rows.Count + " рядків";
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
        private string SelectedId => IdColumn.SelectedIndex <= 0 ? null : IdColumn.SelectedItem as string;
        private void Id_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (features == null) return;
            foreach (var f in features.Where(f => f.Name == SelectedId)) f.Selected = false;
            FeaturesGrid.ItemsSource = features.Where(f => f.Name != SelectedId).ToList();
        }
        private void Seeds_Changed(object sender, RoutedEventArgs e) { if (ready) RefreshSeeds(); }
        private int[] GetSeeds() => SeedMode.SelectedIndex == 0
            ? ComparisonConfig.GenerateSeeds(Integer(BaseSeed, "Базовий seed"), Integer(PairCount, "Кількість пар"))
            : ComparisonConfig.ParseSeeds(CustomSeeds.Text);
        private void RefreshSeeds()
        {
            bool automatic = SeedMode.SelectedIndex == 0;
            PairCount.IsEnabled = automatic; BaseSeed.IsEnabled = automatic; CustomSeeds.IsEnabled = !automatic;
            try
            {
                var seeds = GetSeeds();
                SeedPreview.Text = string.Join(", ", seeds);
                TrainingCount.Text = seeds.Length + " пар / " + ((long)seeds.Length * 2) + " навчань";
                if (!automatic) PairCount.Text = seeds.Length.ToString(CultureInfo.InvariantCulture);
                SeedError.Text = ""; CustomSeeds.BorderBrush = SystemColors.ControlDarkBrush;
            }
            catch (Exception ex)
            {
                SeedPreview.Text = ""; TrainingCount.Text = ""; SeedError.Text = ex.Message;
                CustomSeeds.BorderBrush = Brushes.Firebrick;
            }
        }
        private static int Integer(TextBox box, string name)
        {
            int value;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) throw new ArgumentException(name + ": введіть ціле число.");
            return value;
        }
        private static double Real(TextBox box, string name)
        {
            double value;
            if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw new ArgumentException(name + ": введіть число.");
            return value;
        }
        private ComparisonConfig ReadConfig()
        {
            if (rows == null) throw new ArgumentException("Імпортуйте набір даних.");
            FeaturesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            FeaturesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            return new ComparisonConfig
            {
                Dataset = dataset, IdColumn = SelectedId, Features = features.Where(f => f.Selected && f.Name != SelectedId).Select(f => f.Name).ToList(),
                Normalization = (NormalizationMode)Normalization.SelectedIndex,
                Width = Integer(MapWidth, "Ширина"), Height = Integer(MapHeight, "Висота"), Iterations = Integer(Iterations, "Ітерації"),
                LearningRate = Real(LearningRate, "Швидкість навчання"), Radius = Real(Radius, "Радіус"), Seeds = GetSeeds(),
                SeedMode = SeedMode.SelectedIndex == 0 ? "Automatic" : "Manual", BaseSeed = SeedMode.SelectedIndex == 0 ? Integer(BaseSeed, "Базовий seed") : 0,
                K = Integer(ClusterCount, "k"), KMeansSeed = Integer(KMeansSeed, "Seed k-means"), KMeansIterations = Integer(KMeansIterations, "Ітерації k-means")
            };
        }
        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ReadConfig(); config.Validate();
                SetBusy(true); cancellation = new CancellationTokenSource(); CancelButton.IsEnabled = true;
                ProgressText.Text = "Перевірка даних і нормалізація…";
                var data = await Task.Run(() => PreparedComparisonData.Create(rows, config, cancellation.Token));
                cancellation.Token.ThrowIfCancellationRequested();
                TotalProgress.Maximum = config.Seeds.Length * 2; TotalProgress.Value = 0;
                IterationProgress.Maximum = config.Iterations; IterationProgress.Value = 0;
                var progress = new Progress<ExperimentProgress>(p =>
                {
                    TotalProgress.Value = p.Completed;
                    ProgressText.Text = $"{p.Completed} із {p.Total} навчань · seed {p.Seed} · {ComparisonTables.Lattice(p.Lattice)} · {p.Stage}";
                    IterationProgress.Value = p.Iteration; IterationText.Text = $"Ітерація {p.Iteration} із {config.Iterations}";
                });
                result = await Task.Run(() => ComparisonExperiment.Run(config, data, cancellation.Token, progress));
                RefreshResults();
                ProgressText.Text = (result.Cancelled ? "Скасовано. " : result.Error != null ? "Помилка: " + result.Error + ". " : "Серію завершено. ")
                    + $"{result.CompletedTrainings} із {config.Seeds.Length * 2} навчань; {result.Runs.Count} запусків із показниками. Завершені результати доступні для експорту.";
                Tabs.SelectedIndex = 1;
            }
            catch (OperationCanceledException) { ProgressText.Text = "Скасовано підготовку даних."; }
            catch (Exception ex) { ShowError(ex); }
            finally { cancellation?.Dispose(); cancellation = null; SetBusy(false); }
        }
        private void RefreshResults()
        {
            SeedResults.ItemsSource = ComparisonTables.BySeed(result).DefaultView;
            SummaryResults.ItemsSource = ComparisonTables.Summary(result).DefaultView;
            AriResults.ItemsSource = ComparisonTables.Aris(result).DefaultView;
            AllProfiles.ItemsSource = ComparisonTables.Profiles(result).DefaultView;
            ProfileSeed.ItemsSource = result.Config.Seeds.Where(s => result.Runs.Count(r => r.Seed == s) == 2).ToArray();
            ProfileSeed.SelectedIndex = 0;
            if (ProfileSeed.Items.Count == 0) { MatchedResults.ItemsSource = null; ProfileHint.Text = "н/д — немає повної пари для зіставлення."; }
            else ProfileHint.Text = "Максимальний сумарний перетин абонентів. Середні — вихідні значення; н/д — порожній кластер.";
        }
        private void ProfileSeed_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (result != null && ProfileSeed.SelectedItem is int seed) MatchedResults.ItemsSource = ComparisonTables.MatchedProfiles(result, seed).DefaultView;
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { cancellation?.Cancel(); CancelButton.IsEnabled = false; }
        private void SetBusy(bool value)
        {
            busy = value; SettingsPanel.IsEnabled = !value; StartButton.IsEnabled = !value;
            if (!value) CancelButton.IsEnabled = false;
            ExportButton.IsEnabled = AssignmentsButton.IsEnabled = !value && result != null;
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!busy) return;
            e.Cancel = true; cancellation?.Cancel();
            ProgressText.Text = "Очікування завершення поточної операції. Потім можна закрити вікно; результати залишаться доступними.";
        }
        private void Export_Click(object sender, RoutedEventArgs e) => Export(false);
        private void Assignments_Click(object sender, RoutedEventArgs e) => Export(true);
        private async void Export(bool assignmentsOnly)
        {
            var dialog = new SaveFileDialog { Title = "Виберіть розташування нової папки експорту", FileName = "SOM-comparison", Filter = "Папка експорту|*.export", AddExtension = false };
            if (dialog.ShowDialog(this) != true) return;
            string folder = dialog.FileName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            SetBusy(true);
            try
            {
                await Task.Run(() => ComparisonExport.Write(folder, result, assignmentsOnly));
                ProgressText.Text = "Експорт збережено: " + folder;
            }
            catch (Exception ex) { ShowError(ex); }
            finally { SetBusy(false); }
        }
        private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Порівняння решіток SOM", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
