using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using SOM_Kohonen_WpfApp.Models;
using SOM_Kohonen_WpfApp.Service;
using SOM_Kohonen_WpfApp.SOM;
using Color = System.Windows.Media.Color;

namespace SOM_Kohonen_WpfApp.Views
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private Map _map;
		// store selected borders instead of raw grids
		private readonly List<Border> _selectedNodes = new List<Border>();

		// Drag selection state
		private bool _isMouseDown = false;
		private bool _dragSelectMode = true; // true: select, false: deselect

		public MainWindow()
		{
			InitializeComponent();

			StatisticsGrid.Visibility = Visibility.Collapsed;

			try
			{
				string[] activationData = AppDomain.CurrentDomain?.SetupInformation?.ActivationArguments?.ActivationData;
				if (activationData != null && activationData.Any())
				{
					OpenMapFromFile(activationData[0]);
				}
			}
			catch (Exception) { }

			// Listen for mouse up globally to end drag
			this.PreviewMouseUp += MainWindow_MouseUp;
		}

		#region Events
		private async void New_Click(object sender, RoutedEventArgs e)
		{
			SOMWizardWindow wizard = new SOMWizardWindow
			{
				Owner = this
			};
			wizard.Show();
			_map = await wizard.FetchAsync();
			GenerateGrid(_map);
			// Only show data reduction if enabled in the wizard
			if (wizard.ShowDataReduction)
			{
				AnalyzeAndMarkLowInfluenceFeatures();
			}
		}

		private void Open_Click(object sender, RoutedEventArgs e)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "SOM file (*.som)|*.som",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};
			if (openFileDialog.ShowDialog() == true)
			{
				OpenMapFromFile(openFileDialog.FileName);
			}
		}

		private void Save_Click(object sender, RoutedEventArgs e)
		{
			if (MapIsEmpty())
			{
				return;
			}

			SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Filter = "SOM file (*.som)|*.som",
				FileName = $"Map {DateTime.Now:dd.MM.yyyy - HH.mm.ss}.som",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};
			if (saveFileDialog.ShowDialog() == true)
			{
				MapIOService.SaveToFile(saveFileDialog.FileName, _map);
			}
		}

		private void Close_Click(object sender, RoutedEventArgs e)
		{
			if (MapIsEmpty())
			{
				return;
			}

			var messageBoxResult = MessageBox.Show("Do you want to save progress before closing?", "Save the progress", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

			if (messageBoxResult == MessageBoxResult.Cancel)
			{
				return;
			}
			else if (messageBoxResult == MessageBoxResult.Yes)
			{
				SaveFileDialog saveFileDialog = new SaveFileDialog
				{
					Filter = "SOM file (*.som)|*.som",
					FileName = $"Map {DateTime.Now:dd.MM.yyyy - HH.mm.ss}.som",
					InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
				};
				if (saveFileDialog.ShowDialog() == true)
				{
					MapIOService.SaveToFile(saveFileDialog.FileName, _map);
				}
				else
				{
					return;
				}
			}

			MainGrid.Children.Clear();
			_selectedNodes.Clear();
			UpdateStatistics();
			_map = null;
		}

		private void Exit_Click(object sender, RoutedEventArgs e)
		{
			Close();
		}

		private void NodeGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			// sender is a Border now
			var border = sender as Border;
			if (border == null) return;
			_isMouseDown = true;
			if (_selectedNodes.Contains(border))
			{
				_dragSelectMode = false;
				DeselectNode(border);
				_selectedNodes.Remove(border);
			}
			else
			{
				_dragSelectMode = true;
				SelectNode(border);
				_selectedNodes.Add(border);
			}
			// After toggling selection, adjust the outlines for neighboring selections
			UpdateSelectionBorders();
			UpdateStatistics();
		}

		private void NodeGrid_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
		{
			if (!_isMouseDown) return;
			var border = sender as Border;
			if (border == null) return;
			if (_dragSelectMode)
			{
				if (!_selectedNodes.Contains(border))
				{
					SelectNode(border);
					_selectedNodes.Add(border);
					UpdateSelectionBorders();
					UpdateStatistics();
				}
			}
			else
			{
				if (_selectedNodes.Contains(border))
				{
					DeselectNode(border);
					_selectedNodes.Remove(border);
					UpdateSelectionBorders();
					UpdateStatistics();
				}
			}
		}

		private void MainWindow_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			_isMouseDown = false;
		}

		private void ClearSelection_Click(object sender, RoutedEventArgs e)
		{
			foreach (var border in _selectedNodes.ToList())
			{
				DeselectNode(border);
			}
			_selectedNodes.Clear();
			UpdateSelectionBorders();
			UpdateStatistics();
		}

		private void ShowTextMappings_Click(object sender, RoutedEventArgs e)
		{
			if (_map?.TextValueMappings == null || _map.TextValueMappings.Count == 0)
			{
				MessageBox.Show("No text mappings available.", "Text Mappings", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			var rows = new List<TextMappingRow>();
			foreach (var param in _map.TextValueMappings)
			{
				string paramName = param.Key;
				foreach (var kv in param.Value)
				{
					rows.Add(new TextMappingRow
					{
						Parameter = paramName,
						OriginalText = kv.Value,
						NumericValue = kv.Key
					});
				}
			}
			var win = new TextMappingsWindow(rows) { Owner = this };
			win.ShowDialog();
		}

		#endregion

		#region Methods

		private void UpdateStatistics()
		{
			if (_selectedNodes.Any())
			{
				StatisticsGrid.Visibility = Visibility.Visible;
				StatisticsDataGrid.ItemsSource = null;
				List<DataCollection> weightsList = new List<DataCollection>();
				foreach (var node in _selectedNodes)
				{
					weightsList.Add(_map[Grid.GetColumn(node), Grid.GetRow(node)].Weights);
				}
				var averages = weightsList[0]
					.Select((dummy, i) => new { dummy.Key, Average = weightsList.Average(inner => inner[i].GetDoubleValue()) })
					.ToList();

				var tableRows = new List<object>();
				for (int i = 0; i < averages.Count; i++)
				{
					string key = averages[i].Key;
					double avg = averages[i].Average;
					string displayValue;
					if (_map.TextValueMappings != null && _map.TextValueMappings.ContainsKey(key) && _map.TextValueMappings[key].Count > 0)
					{
						var closest = _map.TextValueMappings[key]
							.OrderBy(kv => Math.Abs(kv.Key - avg))
							.FirstOrDefault();
						displayValue = closest.Value ?? Math.Round(avg, 2).ToString();
					}
					else
					{
						displayValue = Math.Round(avg, 2).ToString();
					}
					tableRows.Add(new { Value = displayValue, Parameter = key });
				}
				StatisticsDataGrid.ItemsSource = tableRows;
			}
			else
			{
				StatisticsDataGrid.ItemsSource = null;
				StatisticsGrid.Visibility = Visibility.Collapsed;
			}
		}

		private void OpenMapFromFile(string fileName)
		{
			try
			{
				_map = MapIOService.LoadFromFile(fileName);
				if (MapIsEmpty())
				{
					MessageBox.Show("The opened map has zero width or height or depth!", "The opened map has the wrong dimensions", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				GenerateGrid(_map);
				AnalyzeAndMarkLowInfluenceFeatures();
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "The file could not be opened", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		// Utility to sanitize column names for use as UI element names only
		private static string SanitizeColumnName(string columnName)
		{
			var sanitized = System.Text.RegularExpressions.Regex.Replace(columnName, @"[^a-zA-Z0-9]", "");
			if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
				sanitized = "Col" + sanitized;
			return sanitized;
		}

		private void GenerateGrid(Map map)
		{
			_selectedNodes.Clear();
			UpdateStatistics();
			MainGrid.Children.Clear();
			ViewMenu.Items.Clear();
			MapSeedLabel.Content = "";
			MapSeedLabel.Visibility = Visibility.Collapsed;
			if (MapIsEmpty())
			{
				MessageBox.Show("Map is empty");
				return;
			}

			MenuItem showMapSeedMenuItem = new MenuItem
			{
				IsCheckable = true,
				IsChecked = Properties.Settings.Default.ShowMapSeedInResults,
				Header = "Show map seed"
			};
			showMapSeedMenuItem.Click += (s, e) =>
			{
				Properties.Settings.Default.ShowMapSeedInResults = !Properties.Settings.Default.ShowMapSeedInResults;
				Properties.Settings.Default.Save();
				if (Properties.Settings.Default.ShowMapSeedInResults)
				{
					MapSeedLabel.Visibility = Visibility.Visible;
				}
				else
				{
					MapSeedLabel.Visibility = Visibility.Collapsed;
				}
			};

			ViewMenu.Items.Add(showMapSeedMenuItem);
			ViewMenu.Items.Add(new Separator());

			// Add Clear Selection button
			MenuItem clearSelectionMenuItem = new MenuItem
			{
				Header = "Clear Selection"
			};
			clearSelectionMenuItem.Click += ClearSelection_Click;
			ViewMenu.Items.Add(clearSelectionMenuItem);
			ViewMenu.Items.Add(new Separator());

			if (Properties.Settings.Default.ShowMapSeedInResults)
			{
				MapSeedLabel.Visibility = Visibility.Visible;
			}

			MapSeedLabel.Content = $"Map seed: {map.Seed}";

			List<FrameworkElement> gridNodes = new List<FrameworkElement>();
			double hexWidth = 15; // width of hexagon
			double hexHeight = Math.Sqrt(3) / 2 * hexWidth; // height of hexagon
			for (int i = 0; i < _map[0, 0].Weights.Count; i++)
			{
				FrameworkElement grid;
				string mapKey = _map[0, 0].Weights[i].Key; // Use original key for display
				string sanitizedKey = SanitizeColumnName(mapKey); // Only for UI element names

				MenuItem menuItem = new MenuItem
				{
					Name = $"{sanitizedKey}MenuItem",
					Header = $"{mapKey}MenuItem", // Display original key
					IsCheckable = true,
					IsChecked = true
				};
				menuItem.Click += ColumnsVisibilityMenuItem_Click;
				ViewMenu.Items.Add(menuItem);

				int gridWidth;
				int gridHeight;

				if (map.NodeType == NodeType.Hexagonal)
				{
					// Use Canvas for hexagonal layout
					gridWidth = (int)(map.Width * hexWidth + hexWidth / 2);
					gridHeight = (int)((map.Height + 0.4) * hexHeight);
					Canvas canvas = new Canvas
					{
						Width = gridWidth,
						Height = gridHeight,
						HorizontalAlignment = HorizontalAlignment.Left,
						VerticalAlignment = VerticalAlignment.Top,
						Background = new SolidColorBrush(Colors.Transparent)
					};
					grid = canvas;
				}
				else
				{
					gridWidth = map.Width * 10;
					gridHeight = map.Height * 10;
					Grid gridSquare = CreateGrid(Colors.Gray, gridWidth, gridHeight);
					for (int x = 0; x < map.Width; x++)
					{
						gridSquare.ColumnDefinitions.Add(new ColumnDefinition());
					}
					for (int y = 0; y < map.Height; y++)
					{
						gridSquare.RowDefinitions.Add(new RowDefinition());
					}
					grid = gridSquare;
				}
				gridNodes.Add(grid);

				TextBlock txtBlock = new TextBlock
				{
					Text = mapKey, // Display original key
					FontSize = 14,
					FontWeight = FontWeights.Bold,
					Foreground = new SolidColorBrush(Colors.Black),
					Background = new SolidColorBrush(Colors.LightGray),
					VerticalAlignment = VerticalAlignment.Top,
					Padding = new Thickness(5)
				};

				StackPanel stackPanel = new StackPanel
				{
					MinWidth = gridWidth,
					MaxWidth = gridHeight,
					Orientation = Orientation.Vertical
				};

				stackPanel.Children.Add(txtBlock);
				stackPanel.Children.Add(grid);

				Border border = new Border
				{
					BorderThickness = new Thickness(4),
					BorderBrush = new SolidColorBrush(Colors.LightGray),
					Child = stackPanel,
					Margin = new Thickness(5),
					Padding = new Thickness(2, 0, 2, 2)
				};

				MainGrid.Children.Add(border);
				try
				{
					UnregisterName($"{sanitizedKey}Grid");
				}
				catch (Exception) { }
				RegisterName($"{sanitizedKey}Grid", border);
			}

			for (int x = 0; x < map.Width; x++)
			{
				for (int y = 0; y < map.Height; y++)
				{
					Node mapNode = map[x, y];
					for (int i = 0; i < map.Depth; i++)
					{
						if (map.NodeType == NodeType.Hexagonal)
						{
							double xOffset = (y % 2 == 0) ? 0 : hexWidth / 2;
							double left = x * hexWidth + xOffset;
							double top = y * hexHeight;
							Grid inner;
							if (mapNode == null || mapNode.Weights == null || mapNode.Weights.Count == 0)
							{
								inner = CreateGrid(ColorFromHex("#e6e6e6"), (int)hexWidth, (int)hexHeight);
							}
							else
							{
								double maxVal = mapNode.Weights[i].MaxCollectionValue;
								double val = mapNode.Weights[i].GetDoubleValue();
								double ratio = maxVal == 0 ? 0 : val / maxVal; // 0..1
								Color color = GetHeatMapColor(ratio);
								inner = CreateGrid(color, (int)hexWidth, (int)hexHeight);
							}
							Border cell = CreateCellBorderHex(inner, hexWidth);
							cell.MouseDown += NodeGrid_MouseDown;
							cell.MouseEnter += NodeGrid_MouseEnter;
							// Store coordinates for selection/statistics
							cell.SetValue(Grid.ColumnProperty, x);
							cell.SetValue(Grid.RowProperty, y);
							Canvas.SetLeft(cell, left);
							Canvas.SetTop(cell, top);
							((Canvas)gridNodes[i]).Children.Add(cell);
						}
						else
						{
							Grid inner;
							if (mapNode == null || mapNode.Weights == null || mapNode.Weights.Count == 0)
							{
								inner = CreateGrid(ColorFromHex("#e6e6e6"));
							}
							else
							{
								double maxVal = mapNode.Weights[i].MaxCollectionValue;
								double val = mapNode.Weights[i].GetDoubleValue();
								double ratio = maxVal == 0 ? 0 : val / maxVal; // 0..1
								Color color = GetHeatMapColor(ratio);
								inner = CreateGrid(color);
							}
							Border cell = CreateCellBorder(inner);
							cell.Margin = new Thickness(0, 0, x + 1 != map.Width ? 1 : 0, y + 1 != map.Height ? 1 : 0);
							Grid.SetColumn(cell, x);
							Grid.SetRow(cell, y);
							cell.MouseDown += NodeGrid_MouseDown;
							cell.MouseEnter += NodeGrid_MouseEnter;
							((Grid)gridNodes[i]).Children.Add(cell);
						}
					}
				}
			}
		}

		private void ColumnsVisibilityMenuItem_Click(object sender, RoutedEventArgs e)
		{
			MenuItem menuItem = (MenuItem)sender;
			string sanitizedKey = menuItem.Name.Replace("MenuItem", "");
			try
			{
				if (menuItem.IsChecked)
				{
					((Border)FindName($"{sanitizedKey}Grid")).Visibility = Visibility.Visible;
				}
				else
				{
					((Border)FindName($"{sanitizedKey}Grid")).Visibility = Visibility.Collapsed;
				}
			}
			catch (Exception) { }
		}

		// helper to create a cell border wrapping a grid and store original background in Tag
		private Border CreateCellBorder(Grid inner)
		{
			var originalBrush = inner.Background as SolidColorBrush;
			var cell = new Border
			{
				Child = inner,
				BorderBrush = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(0),
				Tag = originalBrush // store for restore
			};
			return cell;
		}

		private Border CreateCellBorderHex(Grid inner, double size = 10)
		{
			var cell = new Border
			{
				Child = inner,
				BorderBrush = Brushes.Transparent,
				BorderThickness = new Thickness(0),
				Padding = new Thickness(0),
				Tag = inner.Background as SolidColorBrush
			};
			// Add hexagon shape overlay
			var hex = new Polygon
			{
				Stroke = Brushes.Gray,
				StrokeThickness = 1,
				Fill = inner.Background
			};

			// Calculate the coordinates of the hexagon
			// Center at (size / 2, size / 2), height = size, width ≈ size * sqrt(3) / 2
			double width = Math.Sqrt(3) / 2 * size;

			hex.Points = new PointCollection
{
	new Point(width / 2, 0),                  // top point
    new Point(width, size / 4),               // top-right
    new Point(width, 3 * size / 4),           // bottom-right
    new Point(width / 2, size),               // bottom
    new Point(0, 3 * size / 4),               // bottom-left
    new Point(0, size / 4)                    // top-left
};

			var grid = new Grid();
			grid.Children.Add(hex);
			cell.Child = grid;
			return cell;
		}

		private Grid CreateGrid(Color color, int width = 10, int height = 10)
		{
			return new Grid
			{
				Width = width,
				Height = height,
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top,
				Background = new SolidColorBrush(color)
			};
		}

		// Parses #RRGGBB or #AARRGGBB
		private Color ColorFromHex(string hex)
		{
			if (string.IsNullOrWhiteSpace(hex)) return Colors.Transparent;
			hex = hex.Trim();
			if (hex.StartsWith("#")) hex = hex.Substring(1);
			byte a = 255;
			int start = 0;
			if (hex.Length == 8)
			{
				a = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
				start = 2;
			}
			if (hex.Length - start != 6) return Colors.Transparent;
			byte r = byte.Parse(hex.Substring(start, 2), NumberStyles.HexNumber);
			byte g = byte.Parse(hex.Substring(start + 2, 2), NumberStyles.HexNumber);
			byte b = byte.Parse(hex.Substring(start + 4, 2), NumberStyles.HexNumber);
			return Color.FromArgb(a, r, g, b);
		}

		/// <summary>
		/// Returns a color on a heatmap from blue (0) to red (1) using HSV interpolation.
		/// </summary>
		private Color GetHeatMapColor(double value)
		{
			value = Math.Max(0.0, Math.Min(1.0, value));
			double hue = 240.0 * (1.0 - value); // 240 (blue) -> 0 (red)
			return FromHSV(hue, 1.0, 1.0);
		}

		// HSV to RGB conversion (h in degrees 0..360, s/v 0..1)
		private Color FromHSV(double h, double s, double v)
		{
			double c = v * s;
			double hh = h / 60.0;
			double x = c * (1 - Math.Abs(hh % 2 - 1));
			double r1 = 0, g1 = 0, b1 = 0;
			if (hh >= 0 && hh < 1) { r1 = c; g1 = x; b1 = 0; }
			else if (hh >= 1 && hh < 2) { r1 = x; g1 = c; b1 = 0; }
			else if (hh >= 2 && hh < 3) { r1 = 0; g1 = c; b1 = x; }
			else if (hh >= 3 && hh < 4) { r1 = 0; g1 = x; b1 = c; }
			else if (hh >= 4 && hh < 5) { r1 = x; g1 = 0; b1 = c; }
			else { r1 = c; g1 = 0; b1 = x; }
			double m = v - c;
			byte r = (byte)Math.Round((r1 + m) * 255);
			byte g = (byte)Math.Round((g1 + m) * 255);
			byte b = (byte)Math.Round((b1 + m) * 255);
			return Color.FromArgb(255, r, g, b);
		}

		private bool MapIsEmpty()
		{
			return _map == null || _map.Width == 0 || _map.Height == 0 || _map.Depth == 0;
		}

		// Call this after training and grid generation to analyze and mark low-influence features
		private void AnalyzeAndMarkLowInfluenceFeatures()
		{
			if (_map == null) return;

			// 1. Identify low-influence features
			var lowImpact = AnalyzeLowInfluenceFeatures();

			// 2. Mark in UI: change label color and add tag for low-influence features
			bool anyMarked = false;
			foreach (var child in MainGrid.Children)
			{
				if (child is Border border && border.Child is StackPanel panel && panel.Children[0] is TextBlock txt)
				{
					string feature = txt.Text.Replace(" (Low Influence)", "");
					if (lowImpact.Contains(feature))
					{
						txt.Foreground = new SolidColorBrush(Colors.Red); // Mark as low influence
						txt.Text = feature + " (Low Influence)";
						anyMarked = true;
					}
					else
					{
						txt.Foreground = new SolidColorBrush(Colors.Black); // Reset others
						txt.Text = feature;
					}
				}
			}

			// 3. Show message if none found
			if (!anyMarked)
			{
				MessageBox.Show("No parameters with low influence were found.", "Feature Reduction", MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		/// <summary>
		/// Identifies low influence features, based on lowest 20% variance across all nodes.
		/// </summary>
		/// <returns>List of feature names with low influence on the map.</returns>
		private List<string> AnalyzeLowInfluenceFeatures()
		{
			if (_map == null) return new List<string>();

			// 1. Calculate variance for each feature
			var variances = _map.CalculateFeatureVariance();
			if (variances.Count == 0) return new List<string>();

			// Dynamically set threshold so that about 20% of features are marked as low influence
			// Sort variances and use the 20% as the cutoff
			var sortedVars = variances.OrderBy(kv => kv.Value).ToList();
			int n = sortedVars.Count;
			int cutoffIndex = (int)Math.Ceiling(n * 0.2) - 1; // 20%
			if (cutoffIndex < 0) cutoffIndex = 0;
			double threshold = sortedVars[cutoffIndex].Value;

			// 2. Identify low-influence features based on the provided threshold
			var lowImpact = variances.Where(kv => kv.Value <= threshold).Select(kv => kv.Key).ToList();
			return lowImpact;
		}

		#endregion

		// brighten the cell background (blend toward white) when selected
		private void SelectNode(Border cell)
		{
			if (cell == null) return;
			var inner = cell.Child as Grid;
			if (inner == null) return;
			var original = cell.Tag as SolidColorBrush;
			if (original == null) return;

			// Hexagonal selection: overlay hex border only, no fill, no background highlight
			if (_map != null && _map.NodeType == NodeType.Hexagonal)
			{
				inner.Background = Brushes.Transparent;
				// Remove any previous selection border
				var oldHex = inner.Children.OfType<Polygon>().FirstOrDefault(p => (string)p.Tag == "HexSelectionBorder");
				if (oldHex != null) inner.Children.Remove(oldHex);
				// The actual border will be drawn in UpdateSelectionBorders
				cell.BorderBrush = Brushes.Transparent;
			}
			else
			{
				var bright = new SolidColorBrush(BlendColors(original.Color, Colors.White, 0.25));
				inner.Background = bright;
				cell.BorderBrush = new SolidColorBrush(Colors.Black);
			}
		}

		private void DeselectNode(Border cell)
		{
			if (cell == null) return;
			var inner = cell.Child as Grid;
			if (inner == null) return;
			var original = cell.Tag as SolidColorBrush;
			if (_map != null && _map.NodeType == NodeType.Hexagonal)
			{
				// Remove all selection border overlays (Polylines/Polygons with tag)
				var toRemove = inner.Children.OfType<UIElement>()
					.Where(e => (e is Polyline || e is Polygon) && (string)((dynamic)e).Tag == "HexSelectionBorder")
					.ToList();
				foreach (var el in toRemove)
					inner.Children.Remove(el);
				// Remove all children except the original hexagon Polygon
				var hexagon = inner.Children.OfType<Polygon>().FirstOrDefault(p => p.Stroke == Brushes.Gray);
				inner.Children.Clear();
				if (hexagon != null)
				{
					inner.Children.Add(hexagon);
					hexagon.Fill = original ?? Brushes.Transparent;
				}
				cell.BorderBrush = Brushes.Transparent;
				cell.BorderThickness = new Thickness(0);
			}
			else
			{
				if (original != null)
				{
					inner.Background = original;
				}
				cell.BorderBrush = Brushes.Transparent;
				cell.BorderThickness = new Thickness(0);
			}
		}

		// Recompute border for all selected nodes so internal borders are hidden (hex version)
		private void UpdateSelectionBorders()
		{
			if (_map != null && _map.NodeType == NodeType.Hexagonal)
			{
				// Remove all selection borders first
				foreach (var b in _selectedNodes)
				{
					var inner = b.Child as Grid;
					if (inner == null) continue;
					var toRemove = inner.Children.OfType<UIElement>()
						.Where(e => (e is Polyline || e is Polygon) && (string)((dynamic)e).Tag == "HexSelectionBorder")
						.ToList();
					foreach (var el in toRemove)
						inner.Children.Remove(el);
				}
				// Points for a flat-topped hexagon in strict clockwise order starting from top
				// 0: top, 1: top-right, 2: bottom-right, 3: bottom, 4: bottom-left, 5: top-left
				// Directions for even-q vertical layout, matching the points order
				int[][] evenDirs = new int[][] { new[] { 0, -1 }, new[] { 1, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { -1, -1 } };
				int[][] oddDirs = new int[][] { new[] { 0, -1 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 1 }, new[] { -1, 0 } };
				var set = new HashSet<(int x, int y)>();
				foreach (var b in _selectedNodes)
				{
					set.Add((Grid.GetColumn(b), Grid.GetRow(b)));
				}
				foreach (var b in _selectedNodes)
				{
					int x = Grid.GetColumn(b);
					int y = Grid.GetRow(b);
					var inner = b.Child as Grid;
					if (inner == null) continue;
					double size = inner.Width > 0 ? inner.Width : 15;
					double width = Math.Sqrt(3) / 2 * size;
					// Points in strict clockwise order (flat-topped)
					var points = new[]
					{
						new Point(width / 2, 0), // 0 top
						new Point(width, size / 4), // 1 top-right
						new Point(width, 3 * size / 4), // 2 bottom-right
						new Point(width / 2, size), // 3 bottom
						new Point(0, 3 * size / 4), // 4 bottom-left
						new Point(0, size / 4) // 5 top-left
					};
					var dirs = (y % 2 == 0) ? evenDirs : oddDirs;
					for (int i = 0; i < 6; i++)
					{
						int nx = x + dirs[i][0];
						int ny = y + dirs[i][1];
						if (!set.Contains((nx, ny)))
						{
							var poly = new Polyline
							{
								Stroke = Brushes.Black,
								StrokeThickness = 2,
								Points = new PointCollection { points[i], points[(i + 1) % 6] },
								Tag = "HexSelectionBorder"
							};
							inner.Children.Add(poly);
						}
					}
					b.BorderBrush = Brushes.Transparent;
					b.BorderThickness = new Thickness(0);
				}
			}
			else
			{
				int highlightThickness = 2;
				var set = new HashSet<(int x, int y)>();
				foreach (var b in _selectedNodes)
				{
					set.Add((Grid.GetColumn(b), Grid.GetRow(b)));
				}
				foreach (var b in _selectedNodes)
				{
					int x = Grid.GetColumn(b);
					int y = Grid.GetRow(b);
					bool left = set.Contains((x - 1, y));
					bool right = set.Contains((x + 1, y));
					bool top = set.Contains((x, y - 1));
					bool bottom = set.Contains((x, y + 1));

					double leftT = left ? 0 : highlightThickness;
					double topT = top ? 0 : highlightThickness;
					double rightT = right ? 0 : highlightThickness;
					double bottomT = bottom ? 0 : highlightThickness;
					b.BorderThickness = new Thickness(leftT, topT, rightT, bottomT);
					b.BorderBrush = new SolidColorBrush(Colors.Black);
				}
			}
		}

		// blend two colors by t (0..1)
		private Color BlendColors(Color a, Color b, double t)
		{
			t = Math.Max(0, Math.Min(1, t));
			byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
			byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
			byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
			byte aa = (byte)Math.Round(a.A + (b.A - a.A) * t);
			return Color.FromArgb(aa, r, g, bl);
		}

		private async void STConverter_Click(object sender, RoutedEventArgs e)
		{
			StatisticaDataConverterWindow wizard = new StatisticaDataConverterWindow
			{
				Owner = this
			};
			wizard.Show();
			_map = await wizard.FetchAsync();
			GenerateGrid(_map);
		}
	}
}
