using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SOM_Kohonen_WpfApp.Models;
using SOM_Kohonen_WpfApp.Models.Settings;
using SOM_Kohonen_WpfApp.SOM;

namespace SOM_Kohonen_WpfApp.Views
{
	/// <summary>
	/// Interaction logic for StatisticaDataConverterWindow.xaml
	/// </summary>
	public partial class StatisticaDataConverterWindow : Window
	{
		private List<Dictionary<string, object>> jsonData;
		private List<DataColumn> dataColumns;
		private Map _map;

		public StatisticaDataConverterWindow()
		{
			InitializeComponent();
		}

		private void Result_Click(object sender, RoutedEventArgs e)
		{
			CreateMapFromJsonData();
			MessageBox.Show($"Width: {_map.Width}\nHeight: {_map.Height}\nDepth: {_map.Depth}");
			// Set both the map and the checkbox state for the result
			_tcs.SetResult(_map);
			Close();
		}

		private void ImportSettingsSelectData_Click(object sender, RoutedEventArgs e)
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Json file (*.json)|*.json",
				InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
			};
			if (openFileDialog.ShowDialog() != true)
			{
				return;
			}

			// Getting data from json
			jsonData = GetJsonData(openFileDialog.FileName);

			InputFileNameLabel.Content = openFileDialog.FileName + "   Count: " + jsonData.Count;

			List<string> columns = new List<string>();
			foreach (var item in jsonData)
			{
				columns.AddRange(item.Keys);
			}
			columns = columns.Distinct().ToList();

			dataColumns = new List<DataColumn>();
			foreach (var item in columns)
			{
				dataColumns.Add(new DataColumn
				{
					MainColumn = item,
					Column = item,
					InputOption = item.ToLower() == "code" || item.ToLower() == "id" || item.ToLower() == "код" || item.ToLower() == "neuron id" || item.ToLower() == "activation" ? InputOption.Info : item.ToLower() == "neuron location" ? InputOption.XY : InputOption.Input
				});
			}

			DataColumnsDataGrid.ItemsSource = dataColumns;
		}

		private static List<Dictionary<string, object>> GetJsonData(string path)
		{
			string json = File.ReadAllText(path);
			JArray parsedArray = JArray.Parse(json);
			List<Dictionary<string, object>> models = new List<Dictionary<string, object>>();
			foreach (JObject parsedObject in parsedArray.Children<JObject>())
			{
				models.Add(parsedObject.ToObject<Dictionary<string, object>>());
			}
			return models;
		}

		private void CreateMapFromJsonData()
		{
			if (jsonData == null || dataColumns == null)
				return;

			// Get input columns
			var inputColumns = dataColumns.Where(c => c.InputOption == InputOption.Input).Select(c => c.Column).ToList();
			var xyColumn = dataColumns.FirstOrDefault(c => c.InputOption == InputOption.XY)?.Column;
			if (inputColumns.Count == 0 || string.IsNullOrEmpty(xyColumn))
				return;

			// Calculate max value for each input column
			var maxValues = new Dictionary<string, double>();
			foreach (var col in inputColumns)
			{
				double max = double.MinValue;
				foreach (var row in jsonData)
				{
					if (row.ContainsKey(col) && double.TryParse(row[col]?.ToString(), out double val))
					{
						if (val > max) max = val;
					}
				}
				maxValues[col] = max;
			}

			// Find map size from max XY coordinates
			int maxX = 0, maxY = 0;
			foreach (var row in jsonData)
			{
				if (row.ContainsKey(xyColumn))
				{
					var coordStr = row[xyColumn]?.ToString();
					if (!string.IsNullOrEmpty(coordStr) && coordStr.StartsWith("(") && coordStr.EndsWith(")"))
					{
						var parts = coordStr.Trim('(', ')').Split(',');
						if (parts.Length == 2 && int.TryParse(parts[1], out int x) && int.TryParse(parts[0], out int y))
						{
							if (x > maxX) maxX = x;
							if (y > maxY) maxY = y;
						}
					}
				}
			}
			// Map size is max (Statistica index starts from 1)
			var map = new Map(maxX, maxY, new Random().Next());
			map.Depth = inputColumns.Count;

			// Fill nodes from json
			foreach (var row in jsonData)
			{
				if (!row.ContainsKey(xyColumn)) continue;
				var coordStr = row[xyColumn]?.ToString();
				if (string.IsNullOrEmpty(coordStr) || !coordStr.StartsWith("(") || !coordStr.EndsWith(")")) continue;
				var parts = coordStr.Trim('(', ')').Split(',');
				if (parts.Length != 2) continue;
				if (!int.TryParse(parts[1], out int x) || !int.TryParse(parts[0], out int y)) continue;

				// Statistica index starts from 1
				x--;
				y--;

				var weights = new DataCollection();
				foreach (var col in inputColumns)
				{
					if (row.ContainsKey(col))
					{
						weights.Add(new DataModel
						{
							Key = col,
							Value = row[col],
							MaxCollectionValue = maxValues.ContainsKey(col) ? maxValues[col] : 0.0
						});
					}
				}
				map[x, y] = new Node(x, y, weights);
			}
			_map = map;
		}

		private readonly TaskCompletionSource<Map> _tcs = new TaskCompletionSource<Map>();

		public Task<Map> FetchAsync()
		{
			return _tcs.Task;
		}

		public Map Fetch()
		{
			return _tcs.Task.Result;
		}
	}
}
