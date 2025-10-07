using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
		private readonly List<Grid> _selectedNodes = new List<Grid>();

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
			var grid = sender as Grid;
			if (_selectedNodes.Contains(grid))
			{
				grid.Children.Clear();
				_selectedNodes.Remove(grid);
			}
			else
			{
				Grid maskGrid = new Grid
				{
					Background = new SolidColorBrush(Colors.Red)
				};
				grid.Children.Add(maskGrid);
				_selectedNodes.Add(grid);
			}
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

			if (Properties.Settings.Default.ShowMapSeedInResults)
			{
				MapSeedLabel.Visibility = Visibility.Visible;
			}

			MapSeedLabel.Content = $"Map seed: {map.Seed}";

			List<Grid> gridNodes = new List<Grid>();
			for (int i = 0; i < _map[0, 0].Weights.Count; i++)
			{
				Grid grid = CreateGrid(Colors.Gray, map.Width * 10, map.Height * 10);
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

				for (int x = 0; x < map.Width; x++)
				{
					grid.ColumnDefinitions.Add(new ColumnDefinition());
				}
				for (int y = 0; y < map.Height; y++)
				{
					grid.RowDefinitions.Add(new RowDefinition());
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
					MinWidth = map.Width * 10,
					MaxWidth = map.Width * 10,
					Orientation = Orientation.Vertical
				};

				stackPanel.Children.Add(txtBlock);
				stackPanel.Children.Add(grid);

				Border border = new Border
				{
					BorderThickness = new Thickness(4),
					BorderBrush = new SolidColorBrush(Colors.LightGray),
					Child = stackPanel,
					Margin = new Thickness(5)
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
					for (int i = 0; i < mapNode.Weights.Count; i++)
					{
						Grid grid = CreateGrid(ConvertColor(GetColor((int)(mapNode.Weights[i].GetDoubleValue() / mapNode.Weights[i].MaxCollectionValue * 255))));
						grid.Margin = new Thickness(0, 0, x + 1 != map.Width ? 1 : 0, y + 1 != map.Height ? 1 : 0);
						grid.MouseDown += NodeGrid_MouseDown;
						Grid.SetColumn(grid, x);
						Grid.SetRow(grid, y);
						gridNodes[i].Children.Add(grid);
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

		private Color ConvertColor(System.Drawing.Color color)
		{
			return Color.FromArgb(color.A, color.R, color.G, color.B);
		}

		private System.Drawing.Color GetColor(int value)
		{
			if (value < 3)
			{
				return ColorTranslator.FromHtml("#3a34eb");
			}
			else if (value >= 3 && value < 10)
			{
				return ColorTranslator.FromHtml("#4c47e6");
			}
			else if (value >= 10 && value < 20)
			{
				return ColorTranslator.FromHtml("#346ceb");
			}
			else if (value >= 20 && value < 40)
			{
				return ColorTranslator.FromHtml("#5881e0");
			}
			else if (value >= 40 && value < 85)
			{
				return ColorTranslator.FromHtml("#62d1ca");
			}
			else if (value >= 85 && value < 125)
			{
				return ColorTranslator.FromHtml("#95e882");
			}
			else if (value >= 125 && value < 150)
			{
				return ColorTranslator.FromHtml("#d4e882");
			}
			else if (value >= 150 && value < 175)
			{
				return ColorTranslator.FromHtml("#e1e36d");
			}
			else if (value >= 175 && value < 200)
			{
				return ColorTranslator.FromHtml("#e3c76d");
			}
			else if (value >= 200 && value < 225)
			{
				return ColorTranslator.FromHtml("#f2a268");
			}
			else
			{
				return ColorTranslator.FromHtml("#ed544e");
			}
		}

		private bool MapIsEmpty()
		{
			return _map == null || _map.Width == 0 || _map.Height == 0 || _map.Depth == 0;
		}

		// Call this after training and grid generation to analyze and mark low-influence features
		private void AnalyzeAndMarkLowInfluenceFeatures()
		{
			if (_map == null) return;
			// 1. Calculate variance for each feature
			var variances = _map.CalculateFeatureVariance();
			if (variances.Count == 0) return;

			// Dynamically set threshold so that about 15–20% of features are marked as low influence
			// Sort variances and use the 20th percentile as the cutoff
			var sortedVars = variances.OrderBy(kv => kv.Value).ToList();
			int n = sortedVars.Count;
			int cutoffIndex = (int)Math.Ceiling(n * 0.2) - 1; // 20th percentile
			if (cutoffIndex < 0) cutoffIndex = 0;
			double threshold = sortedVars[cutoffIndex].Value;
			// Only features with variance <= threshold are marked as low influence

			// 2. Identify low-influence features
			var lowImpact = variances.Where(kv => kv.Value <= threshold).Select(kv => kv.Key).ToList();

			// 3. Mark in UI: change label color and add tag for low-influence features
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

			// 4. Show message if none found
			if (!anyMarked)
			{
				MessageBox.Show("No parameters with low influence were found.", "Feature Reduction", MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}

		#endregion
	}
}
