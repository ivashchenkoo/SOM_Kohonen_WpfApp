using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SOM_Kohonen_WpfApp.Service;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Views
{
    public sealed class SeedFeatureMaps
    {
        public int Seed { get; }
        public LatticeFeatureMaps Hex { get; }
        public LatticeFeatureMaps Square { get; }
        public SeedFeatureMaps(IGrouping<int, FeatureMapPair> group)
        {
            Seed = group.Key;
            var features = group.ToList();
            Hex = new LatticeFeatureMaps(NodeType.Hexagonal, features, features[0].Hex != null);
            Square = new LatticeFeatureMaps(NodeType.Square, features, features[0].Square != null);
        }
    }

    public sealed class LatticeFeatureMaps
    {
        public NodeType Lattice { get; }
        public List<FeatureMapPair> Features { get; }
        public bool Available { get; }
        public string Heading { get; }
        internal LatticeFeatureMaps(NodeType lattice, List<FeatureMapPair> features, bool available)
        {
            Lattice = lattice;
            Features = features;
            Available = available;
            Heading = ComparisonTables.MapHeading(features[0].Result, features[0].Seed, lattice);
        }
    }

    public sealed class FeatureMapPair
    {
        public ComparisonResult Result { get; private set; }
        public int Seed { get; private set; }
        public int Feature { get; private set; }
        public ComparisonRun Square { get; private set; }
        public ComparisonRun Hex { get; private set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public string Name => Result.Config.Features[Feature];
        public double MapHeight => Math.Min(220, Math.Max(112, Result.Config.Height * 15.0));
        public bool Constant => Minimum == Maximum;
        public string LowLabel => SourceValue(Minimum).ToString("G6");
        public string HighLabel => SourceValue(Maximum).ToString("G6");
        public double SourceValue(double weight) => weight * Result.Data.Scales[Feature] + Result.Data.Offsets[Feature];
        public double Intensity(double weight) => Constant ? 0.5 : (weight - Minimum) / (Maximum - Minimum);

        public static List<FeatureMapPair> Build(ComparisonResult result)
        {
            var pairs = new List<FeatureMapPair>();
            if (result.Runs.Count == 0) return pairs;
            int depth = result.Config.Features.Count;
            var low = Enumerable.Repeat(double.PositiveInfinity, depth).ToArray();
            var high = Enumerable.Repeat(double.NegativeInfinity, depth).ToArray();
            foreach (var run in result.Runs)
                foreach (var weights in run.NodeWeights)
                    for (int j = 0; j < depth; j++)
                    {
                        low[j] = Math.Min(low[j], weights[j]);
                        high[j] = Math.Max(high[j], weights[j]);
                    }
            var bySeed = result.Runs.ToLookup(r => r.Seed);
            foreach (int seed in result.Config.Seeds)
            {
                var square = bySeed[seed].FirstOrDefault(r => r.Lattice == NodeType.Square);
                var hex = bySeed[seed].FirstOrDefault(r => r.Lattice == NodeType.Hexagonal);
                if (square == null && hex == null) continue;
                for (int j = 0; j < depth; j++)
                    pairs.Add(new FeatureMapPair { Result = result, Seed = seed, Feature = j, Square = square,
                        Hex = hex, Minimum = low[j], Maximum = high[j] });
            }
            return pairs;
        }
    }

    // Render the entire component plane as one WPF visual, including for off-screen PNG rendering.
    public sealed class FeaturePlane : FrameworkElement
    {
        public static readonly DependencyProperty SelectionVersionProperty = DependencyProperty.Register(
            nameof(SelectionVersion), typeof(int), typeof(FeaturePlane),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
        public int SelectionVersion { get => (int)GetValue(SelectionVersionProperty); set => SetValue(SelectionVersionProperty, value); }
        public static readonly DependencyProperty LatticeProperty = DependencyProperty.Register(
            nameof(Lattice), typeof(NodeType), typeof(FeaturePlane),
            new FrameworkPropertyMetadata(NodeType.Square, FrameworkPropertyMetadataOptions.AffectsRender,
                (sender, e) => ((FeaturePlane)sender).heatmap = null));
        public NodeType Lattice { get => (NodeType)GetValue(LatticeProperty); set => SetValue(LatticeProperty, value); }
        public event EventHandler SelectionEdited;
        private FeatureMapPair Pair => DataContext as FeatureMapPair;
        private ComparisonRun Run => Lattice == NodeType.Hexagonal ? Pair?.Hex : Pair?.Square;
        private static readonly Brush[] Palette = Enumerable.Range(0, 257).Select(i =>
        {
            var brush = new SolidColorBrush(MainWindow.GetHeatMapColor(i / 256.0));
            brush.Freeze();
            return (Brush)brush;
        }).ToArray();
        private const double HexHeight = 1.1547005383792515;
        private static readonly StreamGeometry Hexagon = MakeHexagon();
        private static readonly Pen SelectedBorder = new Pen(Brushes.Black, 0.13);
        private static readonly Pen CellBorder = new Pen(Brushes.White, 0.025);
        private DrawingGroup heatmap;
        private bool selecting;

        public FeaturePlane()
        {
            ClipToBounds = true;
            DataContextChanged += (s, e) => { heatmap = null; ToolTip = null; InvalidateVisual(); };
        }

        private static StreamGeometry MakeHexagon()
        {
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(new Point(0.5, 0), true, true);
                g.PolyLineTo(new[] { new Point(1, HexHeight/4), new Point(1, 3*HexHeight/4),
                    new Point(0.5, HexHeight), new Point(0, 3*HexHeight/4), new Point(0, HexHeight/4) }, true, true);
            }
            shape.Freeze();
            return shape;
        }

        private Matrix Projection()
        {
            int w = Pair.Result.Config.Width, h = Pair.Result.Config.Height;
            double hexExtent = (h - 1) * 0.75 * HexHeight + HexHeight;
            double scale = Math.Min(Math.Max(1, ActualWidth - 8) / (w + 0.5),
                Math.Max(1, ActualHeight - 8) / Math.Max(h, hexExtent));
            bool hex = Lattice == NodeType.Hexagonal;
            return new Matrix(scale, 0, 0, scale, (ActualWidth - scale * (hex && h > 1 ? w + 0.5 : w)) / 2,
                (ActualHeight - scale * (hex ? hexExtent : h)) / 2);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
            if (Pair == null) return;
            var run = Run;
            if (run == null)
            {
                var label = new FormattedText("Запуск не завершено", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, Brushes.SlateGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point((ActualWidth - label.Width)/2, (ActualHeight - label.Height)/2));
                return;
            }
            if (heatmap == null)
            {
                heatmap = new DrawingGroup();
                using (var layer = heatmap.Open())
                    for (int i = 0; i < run.NodeWeights.Length; i++)
                    {
                        double ratio = Pair.Intensity(run.NodeWeights[i][Pair.Feature]);
                        var color = Palette[(int)Math.Round(256 * Math.Min(1, Math.Max(0, ratio)))];
                        DrawNode(layer, i, color, CellBorder);
                    }
                heatmap.Freeze();
            }
            dc.PushTransform(new MatrixTransform(Projection()));
            dc.DrawDrawing(heatmap);
            foreach (int i in run.SelectedNodes) DrawNode(dc, i, null, SelectedBorder);
            dc.Pop();
        }

        private void DrawNode(DrawingContext dc, int index, Brush fill, Pen outline)
        {
            int height = Pair.Result.Config.Height;
            int x = index / height, y = index % height;
            if (Lattice == NodeType.Square)
                dc.DrawRectangle(fill, outline, new Rect(x, y, 1, 1));
            else
            {
                dc.PushTransform(new TranslateTransform(x + 0.5 * (y % 2), y * 0.75 * HexHeight));
                dc.DrawGeometry(fill, outline, Hexagon);
                dc.Pop();
            }
        }

        private int NodeAt(Point cursor)
        {
            if (Run == null) return -1;
            var matrix = Projection();
            matrix.Invert();
            var p = matrix.Transform(cursor);
            var cfg = Pair.Result.Config;
            if (Lattice == NodeType.Square)
            {
                int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
                return x >= 0 && x < cfg.Width && y >= 0 && y < cfg.Height ? x * cfg.Height + y : -1;
            }
            int last = (int)Math.Floor(p.Y / (0.75 * HexHeight));
            for (int y = Math.Max(0, last - 1); y <= Math.Min(cfg.Height - 1, last); y++)
            {
                double shifted = p.X - (y % 2) * 0.5;
                int x = (int)Math.Floor(shifted);
                if (x < 0 || x >= cfg.Width) continue;
                double dy = Math.Abs(p.Y - (y * 0.75 + 0.5) * HexHeight);
                if (dy <= HexHeight * (1 - Math.Abs(shifted - x - 0.5)) / 2) return x * cfg.Height + y;
            }
            return -1;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            int node = NodeAt(e.GetPosition(this));
            if (node < 0) return;
            selecting = !Run.SelectedNodes.Contains(node);
            CaptureMouse();
            Edit(node);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int node = NodeAt(e.GetPosition(this));
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed && node >= 0) Edit(node);
            if (!IsMouseCaptured)
                ToolTip = node < 0 ? null : $"{Pair.Name} = {Pair.SourceValue(Run.NodeWeights[node][Pair.Feature]):G9}  ·  BMU ({node / Pair.Result.Config.Height}, {node % Pair.Result.Config.Height})";
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (IsMouseCaptured) ReleaseMouseCapture();
        }

        private void Edit(int node)
        {
            bool changed = selecting ? Run.SelectedNodes.Add(node) : Run.SelectedNodes.Remove(node);
            if (changed) SelectionEdited?.Invoke(this, EventArgs.Empty);
        }
    }
}
