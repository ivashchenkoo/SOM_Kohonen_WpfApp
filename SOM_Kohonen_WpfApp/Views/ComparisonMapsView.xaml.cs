using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SOM_Kohonen_WpfApp.Service;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Views
{
    public partial class ComparisonMapsView : UserControl
    {
        private ComparisonResult result;
        private MapSelection selection;
        private Canvas dragging;
        private bool addCells;
        private List<FeatureMapPair> featurePairs;
        public static readonly DependencyProperty SelectionVersionProperty = DependencyProperty.Register(
            nameof(SelectionVersion), typeof(int), typeof(ComparisonMapsView));
        public int SelectionVersion { get => (int)GetValue(SelectionVersionProperty); private set => SetValue(SelectionVersionProperty, value); }
        private readonly Dictionary<Polygon, ComparisonRun> cells = new Dictionary<Polygon, ComparisonRun>();
        public ComparisonMapsView()
        {
            InitializeComponent();
            foreach (var canvas in new[] { SquareCanvas, HexCanvas })
            {
                canvas.MouseLeftButtonDown += BeginSelection;
                canvas.MouseMove += ContinueSelection;
                canvas.MouseLeftButtonUp += EndSelection;
                canvas.LostMouseCapture += (s, e) => dragging = null;
            }
        }

        public void Display(ComparisonResult value)
        {
            result = null; // ItemsSource changes can fire SelectionChanged synchronously.
            var seeds = value.Runs.Select(r => r.Seed).Distinct().ToArray();
            SeedPicker.ItemsSource = seeds;
            int? seed = value.ViewedSeed;
            SeedPicker.SelectedItem = seed.HasValue && seeds.Contains(seed.Value) ? seed : seeds.Cast<int?>().FirstOrDefault();
            featurePairs = null;
            AllSeeds.IsChecked = value.ShowAllFeatureSeeds;
            ClusterMode.IsChecked = !value.ViewFeatureMaps;
            FeatureMode.IsChecked = value.ViewFeatureMaps;
            result = value;
            BuildFeatureMenu();
            DatasetName.Text = System.IO.Path.GetFileName(value.Config.Dataset);
            DatasetName.ToolTip = value.Config.Dataset;
            DrawSeed();
            ShowMode();
        }

        private void SeedChanged(object sender, SelectionChangedEventArgs e)
        {
            if (result == null) return;
            DrawSeed();
            if (FeatureMode.IsChecked == true && AllSeeds.IsChecked != true) FilterFeatures();
        }

        private void BuildFeatureMenu()
        {
            FeatureMenu.Items.Clear();
            foreach (var name in result.Config.Features)
            {
                var item = new MenuItem { Header = new TextBlock { Text = name }, Tag = name, IsCheckable = true,
                    StaysOpenOnClick = true, IsChecked = !result.HiddenMapFeatures.Contains(name) };
                item.Click += (s, e) =>
                {
                    if (item.IsChecked) result.HiddenMapFeatures.Remove(name);
                    else result.HiddenMapFeatures.Add(name);
                    FilterFeatures();
                };
                FeatureMenu.Items.Add(item);
            }
        }

        private void MapModeChanged(object sender, RoutedEventArgs e)
        {
            if (result == null) return;
            result.ViewFeatureMaps = FeatureMode.IsChecked == true;
            ShowMode();
        }

        private void ShowMode()
        {
            bool features = FeatureMode.IsChecked == true;
            FeatureControls.Visibility = Gallery.Visibility = features ? Visibility.Visible : Visibility.Collapsed;
            ClusterMaps.Visibility = ClusterLegend.Visibility = ClusterHint.Visibility = features ? Visibility.Collapsed : Visibility.Visible;
            if (features) FilterFeatures();
            else FeatureRows.ItemsSource = null;
        }

        private void AllSeedsChanged(object sender, RoutedEventArgs e)
        {
            if (result == null) return;
            result.ShowAllFeatureSeeds = AllSeeds.IsChecked == true;
            FilterFeatures();
        }

        private void FilterFeatures()
        {
            if (featurePairs == null) featurePairs = FeatureMapPair.Build(result);
            var rows = featurePairs.Where(p => (result.ShowAllFeatureSeeds || p.Seed == result.ViewedSeed)
                && !result.HiddenMapFeatures.Contains(p.Name))
                .GroupBy(p => p.Seed).Select(group => new SeedFeatureMaps(group)).ToList();
            FeatureRows.ItemsSource = rows;
            EmptyFeatures.Text = result.Runs.Count == 0 ? "У серії немає завершених карт." : "Увімкніть ознаку в меню «Показати ознаки».";
            EmptyFeatures.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SavePng.IsEnabled = rows.Count > 0;
        }

        private void FeatureSelectionEdited(object sender, EventArgs e)
        {
            var pair = (FeatureMapPair)((FrameworkElement)sender).DataContext;
            if (result.ViewedSeed != pair.Seed) SeedPicker.SelectedItem = pair.Seed;
            else
            {
                foreach (var cell in cells) StyleSelection(cell.Key, cell.Value.SelectedNodes.Contains((int)cell.Key.Tag));
                UpdateMeans();
            }
        }

        private void SaveFeaturePng(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Зображення PNG (*.png)|*.png", DefaultExt = ".png",
                FileName = result.ShowAllFeatureSeeds ? "SOM-features-all" : "SOM-features-" + result.ViewedSeed };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            try { WriteFeaturePng(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "Збереження PNG", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        public void WriteFeaturePng(string path)
        {
            var rows = FeatureRows.ItemsSource as List<SeedFeatureMaps>;
            if (rows == null || rows.Count == 0) throw new InvalidOperationException("Виберіть карти для збереження.");
            const int width = 900;
            const double scale = 1.5;
            var template = (DataTemplate)Resources["FeatureRow"];
            var visual = new DrawingVisual();
            double height = 56;
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                var title = new FormattedText("Карти ознак SOM  ·  " + DatasetName.Text, System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.Black, scale)
                    { MaxTextWidth = width - 24, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
                dc.DrawText(title, new Point(12, 6));
                var units = new FormattedText($"Ваги нейронів у вихідних одиницях  ·  {result.Config.Width} × {result.Config.Height}  ·  {ComparisonTables.OrderName(result.Config.RowOrder)}",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.SlateGray, scale);
                dc.DrawText(units, new Point(12, 31));
                foreach (var row in rows)
                {
                    var presenter = new ContentPresenter { ContentTemplate = template, Content = row, Width = width };
                    presenter.Measure(new Size(width, double.PositiveInfinity));
                    double rowHeight = Math.Ceiling(presenter.DesiredSize.Height);
                    if (width * (height + rowHeight) * scale * scale > 48000000 || (height + rowHeight) * scale > 32000)
                        throw new InvalidOperationException("Для PNG такого розміру виберіть один seed або зменште кількість увімкнених ознак.");
                    presenter.Arrange(new Rect(0, 0, width, rowHeight));
                    presenter.UpdateLayout();
                    var area = new Rect(0, height, width, rowHeight);
                    dc.DrawRectangle(Brushes.White, null, area);
                    dc.DrawRectangle(new VisualBrush(presenter) { ViewboxUnits = BrushMappingMode.Absolute,
                        Viewbox = new Rect(0, 0, width, rowHeight) }, null, area);
                    height += rowHeight;
                }
            }
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)Math.Ceiling(height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = File.Create(temp)) encoder.Save(output);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private void DrawSeed()
        {
            cells.Clear();
            Legend.Children.Clear();
            SquareCanvas.Children.Clear();
            HexCanvas.Children.Clear();
            result.ViewedSeed = SeedPicker.SelectedItem as int?;
            AppliedOrder.Text = ComparisonTables.OrderName(result.Config.RowOrder);
            AppliedOrder.ToolTip = null;
            if (!result.ViewedSeed.HasValue)
            {
                SquareGroup.Header = "Квадратна решітка";
                HexGroup.Header = "Гексагональна решітка";
                selection = null;
                ClearButton.IsEnabled = false;
                MeansGrid.ItemsSource = null;
                SelectionCount.Text = "У серії немає завершених карт.";
                return;
            }
            int seed = result.ViewedSeed.Value;
            SquareGroup.Header = ComparisonTables.MapHeading(result, seed, NodeType.Square);
            HexGroup.Header = ComparisonTables.MapHeading(result, seed, NodeType.Hexagonal);
            selection = new MapSelection(result, seed);
            var runs = result.Runs.Where(r => r.Seed == seed).ToArray();
            var recorded = runs.First();
            if (recorded.FirstSourceRows?.Length > 0)
                AppliedOrder.Text += " · Перші рядки: " + string.Join(", ", recorded.FirstSourceRows);
            AppliedOrder.ToolTip = "SHA-256 порядку рядків: " + recorded.OrderHash;
            var square = runs.FirstOrDefault(r => r.Lattice == NodeType.Square);
            var hex = runs.FirstOrDefault(r => r.Lattice == NodeType.Hexagonal);
            int k = result.Config.K;
            int[] colors = Enumerable.Range(0, k).ToArray();
            int[] hexColors = (int[])colors.Clone();
            bool paired = square != null && hex != null;
            var corresponding = paired ? ComparisonMetrics.MatchClusters(ComparisonMetrics.ClusterOverlaps(square, hex, k)) : colors;
            foreach (int c in colors)
            {
                hexColors[corresponding[c]] = c;
                string label = paired ? $"Кв {c} · Гекс {corresponding[c]}" : $"Кластер {c}";
                Legend.Children.Add(new Border { Background = ClusterBrush(c), Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = label } });
            }
            DrawMap(SquareCanvas, square, colors);
            DrawMap(HexCanvas, hex, hexColors);
            UpdateMeans();
        }

        private SolidColorBrush ClusterBrush(int cluster) =>
            new SolidColorBrush(MainWindow.GetDistinctColor(cluster, result.Config.K));

        private void DrawMap(Canvas canvas, ComparisonRun run, int[] colors)
        {
            if (run == null)
            {
                canvas.Width = 180; canvas.Height = 32;
                canvas.Children.Add(new TextBlock { Text = "Запуск не завершено.", Margin = new Thickness(4) });
                return;
            }
            bool hex = run.Lattice == NodeType.Hexagonal;
            const double width = 24;
            double height = hex ? 2 * width / Math.Sqrt(3) : width;
            double rowStep = hex ? height * 0.75 : height;
            var config = result.Config;
            canvas.Width = config.Width * width + (hex ? width / 2 : 0) + 4;
            canvas.Height = (config.Height - 1) * rowStep + height + 4;
            var points = hex ? new PointCollection { new Point(width/2, 0), new Point(width, height/4),
                new Point(width, 3*height/4), new Point(width/2, height), new Point(0, 3*height/4), new Point(0, height/4) }
                : new PointCollection { new Point(0, 0), new Point(width, 0), new Point(width, height), new Point(0, height) };
            points.Freeze();
            var brushes = colors.Select(ClusterBrush).ToArray();
            for (int i = 0; i < run.NodeClusters.Length; i++)
            {
                int x = i / config.Height, y = i % config.Height;
                double left = 2 + width * x + (hex && y % 2 == 1 ? width / 2 : 0);
                double top = 2 + rowStep * y;
                int hits = selection.Hits(run, i);
                var polygon = new Polygon { Points = points, Tag = i, Fill = brushes[run.NodeClusters[i]], Cursor = Cursors.Hand,
                    ToolTip = $"BMU ({x}, {y}) · кластер {run.NodeClusters[i]} · абонентів {hits}" };
                cells.Add(polygon, run);
                StyleSelection(polygon, run.SelectedNodes.Contains(i));
                Canvas.SetLeft(polygon, left); Canvas.SetTop(polygon, top);
                canvas.Children.Add(polygon);
                if (hits == 0) continue;
                var label = new TextBlock { Text = hits.ToString(), FontSize = 10, IsHitTestVisible = false };
                var box = new Viewbox { Width = width - 4, Height = 14, StretchDirection = StretchDirection.DownOnly,
                    Child = label, IsHitTestVisible = false };
                Canvas.SetLeft(box, left + 2); Canvas.SetTop(box, top + (height - 14) / 2);
                Panel.SetZIndex(box, 2); canvas.Children.Add(box);
            }
        }

        private static void StyleSelection(Polygon shape, bool selected)
        {
            shape.Stroke = selected ? Brushes.Black : Brushes.WhiteSmoke;
            shape.StrokeThickness = selected ? 3 : 0.8;
            Panel.SetZIndex(shape, selected ? 1 : 0);
        }
        private void BeginSelection(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)sender;
            var shape = e.OriginalSource as Polygon;
            if (shape == null) return;
            addCells = !cells[shape].SelectedNodes.Contains((int)shape.Tag);
            if (canvas.CaptureMouse()) dragging = canvas;
            SelectCell(shape);
            e.Handled = true;
        }
        private void ContinueSelection(object sender, MouseEventArgs e)
        {
            if (dragging != sender || e.LeftButton != MouseButtonState.Pressed) return;
            var shape = dragging.InputHitTest(e.GetPosition(dragging)) as Polygon;
            if (shape != null) SelectCell(shape);
        }
        private void EndSelection(object sender, MouseButtonEventArgs e)
        {
            if (dragging == sender) dragging.ReleaseMouseCapture();
        }
        private void SelectCell(Polygon shape)
        {
            var selected = cells[shape].SelectedNodes;
            int index = (int)shape.Tag;
            bool changed = addCells ? selected.Add(index) : selected.Remove(index);
            if (!changed) return;
            StyleSelection(shape, addCells);
            UpdateMeans();
        }
        private void ClearSelection(object sender, RoutedEventArgs e)
        {
            foreach (var pair in cells)
                if (pair.Value.SelectedNodes.Remove((int)pair.Key.Tag)) StyleSelection(pair.Key, false);
            UpdateMeans();
        }
        private void UpdateMeans()
        {
            MeansGrid.ItemsSource = selection.Averages();
            ClearButton.IsEnabled = selection.NodeCount > 0;
            SelectionVersion++;
            string selected = $"Виділено нейронів {selection.NodeCount}";
            if (selection.NodeCount == 1)
            {
                var run = result.Runs.Single(r => r.Seed == result.ViewedSeed && r.SelectedNodes.Count == 1);
                int index = run.SelectedNodes.Single();
                selected = $"{ComparisonTables.Lattice(run.Lattice)} · нейрон ({index / result.Config.Height}, {index % result.Config.Height})";
            }
            SelectionCount.Text = $"Seed {result.ViewedSeed} · {selected} · Унікальних абонентів {selection.SubscriberCount} · Середні у вихідних одиницях";
        }
    }
}
