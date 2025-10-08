using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using SOM_Kohonen_WpfApp.Models;
using SOM_Kohonen_WpfApp.Models.Settings;
using SOM_Kohonen_WpfApp.SOM;
using DataColumn = SOM_Kohonen_WpfApp.Models.Settings.DataColumn;

namespace SOM_Kohonen_WpfApp.Views
{
	/// <summary>
	/// Interaction logic for SOMWizardWindow.xaml
	/// </summary>
	public partial class SOMWizardWindow : Window
	{
		private List<Dictionary<string, object>> jsonData;
		private List<DataColumn> dataColumns;
		private Map _map;
		private readonly BackgroundWorker timerBW = new BackgroundWorker();

		// Mapping for text column encoding (text value to int)
		private Dictionary<string, Dictionary<string, int>> textColumnEncodingMap = new Dictionary<string, Dictionary<string, int>>();
		// Mapping for decoding (int to text value)
		private Dictionary<string, Dictionary<int, string>> textColumnDecodingMap = new Dictionary<string, Dictionary<int, string>>();

		public SOMWizardWindow()
		{
			InitializeComponent();
			ImportSettingsGrid.Visibility = Visibility.Visible;
			NNTrainGrid.Visibility = Visibility.Collapsed;
			ResultButton.IsEnabled = false;

			timerBW.DoWork += TimerWorker_DoWork;
			timerBW.WorkerSupportsCancellation = true;
		}

		private void TimerWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();

			while (!timerBW.CancellationPending)
			{
				Dispatcher.Invoke(() => TimeLabel.Content = $"{stopwatch.Elapsed.Hours}:{stopwatch.Elapsed.Minutes}:{stopwatch.Elapsed.Seconds}:{stopwatch.Elapsed.Milliseconds}");
			}
			stopwatch.Stop();
		}

		private async void Train(Map map, IList<DataCollection> models, double learningRateStart = 0.05, int iterations = 100)
		{
			timerBW.RunWorkerAsync();
			await Task.Run(() =>
			{
				Dispatcher.Invoke(() => TrainButton.IsEnabled = false);
				Stopwatch stopwatch = new Stopwatch();
				stopwatch.Start();
				double latticeRadius = Math.Max(map.Width, map.Height) / 2;
				double timeConstant = iterations / Math.Log(latticeRadius);
				double learningRate = learningRateStart;
				int iteration = 0;

				while (iteration < iterations)
				{
					stopwatch.Restart();
					Dispatcher.Invoke(() => IterationLabel.Content = iteration + 1);

					double neighborhoodRadius = latticeRadius * Math.Exp(-iteration / timeConstant);
					double neighborhoodDiameter = neighborhoodRadius * 2;
					double neighborhoodRadiusSquared = neighborhoodRadius * neighborhoodRadius;

					// Finds the node that best matches the given training data and adjusts the nodes near it based on the given radius and training rate.
					foreach (DataCollection model in models)
					{
						Node bestMatchingNode = map.GetBestMatchingNode(model);

						// Calculate the neighborhood boundaries of map nodes near the best matching node to adjust.
						int startX = (int)Math.Max(0, bestMatchingNode.X - neighborhoodRadius - 1);
						int startY = (int)Math.Max(0, bestMatchingNode.Y - neighborhoodRadius - 1);
						int endX = (int)Math.Min(map.Width, startX + neighborhoodDiameter + 1);
						int endY = (int)Math.Min(map.Height, startY + neighborhoodDiameter + 1);

						for (int x = startX; x < endX; x++)
						{
							for (int y = startY; y < endY; y++)
							{
								Node nodeToAdjust = map[x, y];
								double distanceSquared = bestMatchingNode.DistanceToSquared(nodeToAdjust);

								// Perform a filter to get only the nodes in the neighborhood
								if (distanceSquared <= neighborhoodRadiusSquared)
								{
									double distanceFalloff = Math.Exp(-distanceSquared / (2 * neighborhoodRadiusSquared));
									nodeToAdjust.AdjustWeights(model, learningRate, distanceFalloff);
								}
							}
						}
					}

					iteration++;
					learningRate = learningRateStart * Math.Exp(-(double)iteration / iterations);

					Dispatcher.Invoke(() =>
					{
						LogListView.Items.Add($"Iteration: {iteration}\tLearningRate: {Math.Round(learningRate, 6)}\tTime: {stopwatch.Elapsed.Minutes}:{stopwatch.Elapsed.Seconds}:{stopwatch.Elapsed.Milliseconds}");
						LogScrollViewer.ScrollToBottom();
					});
				}
			});
			Dispatcher.Invoke(() =>
			{
				TrainButton.IsEnabled = true;
				ResultButton.IsEnabled = true;
			});
			timerBW.CancelAsync();
		}

		private void ImportSettingsNext_Click(object sender, RoutedEventArgs e)
		{
			if (jsonData == null || !jsonData.Any() || dataColumns == null)
			{
				return;
			}

			// Removing unneccessary properties
			var columnsToRemove = DataColumnsDataGrid.ItemsSource.Cast<DataColumn>().Where(x => x.InputOption == InputOption.Info);
			foreach (var item in columnsToRemove)
			{
				jsonData.ForEach(x => x.Remove(item.MainColumn));
			}

			dataColumns = DataColumnsDataGrid.ItemsSource.Cast<DataColumn>().Where(x => x.InputOption == InputOption.Input).ToList();

			ImportSettingsGrid.Visibility = Visibility.Collapsed;
			NNTrainGrid.Visibility = Visibility.Visible;
		}

		// Utility to sanitize column names for use as UI element names
		private static string SanitizeColumnName(string columnName)
		{
			var sanitized = Regex.Replace(columnName, @"[^a-zA-Z0-9]", "");
			if (string.IsNullOrEmpty(sanitized) || char.IsDigit(sanitized[0]))
				sanitized = "Col" + sanitized;
			return sanitized;
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

			// Detect text columns and build encoding/decoding maps
			textColumnEncodingMap.Clear();
			textColumnDecodingMap.Clear();
			foreach (var col in columns)
			{
				var values = jsonData.Select(row => row.ContainsKey(col) ? row[col] : null).Where(v => v != null).ToList();
				if (values.Any(v => v is string))
				{
					var unique = values.Select(v => v.ToString()).Distinct().ToList();
					var encoding = new Dictionary<string, int>();
					var decoding = new Dictionary<int, string>();
					for (int i = 0; i < unique.Count; i++)
					{
						encoding[unique[i]] = i;
						decoding[i] = unique[i];
					}
					textColumnEncodingMap[col] = encoding;
					textColumnDecodingMap[col] = decoding;
				}
			}

			dataColumns = new List<DataColumn>();
			foreach (var item in columns)
			{
				dataColumns.Add(new DataColumn
				{
					MainColumn = item,
					Column = item,
					InputOption = item.ToLower() == "code" || item.ToLower() == "id" || item.ToLower() == "код" ? InputOption.Info : InputOption.Input
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

		private void Preview_TextInputInteger(object sender, TextCompositionEventArgs e)
		{
			TextBox textBox = sender as TextBox;

			// Текст, який буде після вставки нового символу
			string newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);

			// Дозволяємо мінус на початку і цифри далі
			Regex regex = new Regex(@"^-?\d*$");
			e.Handled = !regex.IsMatch(newText);
		}

		private void Preview_TextInputDouble(object sender, TextCompositionEventArgs e)
		{
			Regex regex = new Regex(@"^[0-9]*(?:[\,\.][0-9]*)?$");
			e.Handled = !regex.IsMatch(e.Text);
		}

		private void Train_Click(object sender, RoutedEventArgs e)
		{
			LogListView.Items.Clear();

			// Prepare feature set for SOM (one feature per input column)
			var inputColumns = new List<string>();
			foreach (var col in dataColumns.Where(x => x.InputOption == InputOption.Input))
			{
				inputColumns.Add(SanitizeColumnName(col.Column));
			}

			// Initializing training data with one feature per column
			List<DataCollection> dataModels = new List<DataCollection>();
			// Build text value mappings for each text-based parameter
			var textValueMappings = new Dictionary<string, Dictionary<double, string>>();
			foreach (var item in jsonData)
			{
				var dict = new Dictionary<string, object>();
				var vectorizedValues = new List<double>();
				string originalText = string.Empty;
				foreach (var col in dataColumns.Where(x => x.InputOption == InputOption.Input))
				{
					var key = SanitizeColumnName(col.Column);
					if (textColumnEncodingMap.ContainsKey(col.Column))
					{
						var val = item.ContainsKey(col.Column) ? item[col.Column]?.ToString() : null;
						if (val != null && textColumnEncodingMap[col.Column].ContainsKey(val))
						{
							double numVal = textColumnEncodingMap[col.Column][val];
							dict[key] = numVal;
							vectorizedValues.Add(numVal);
							originalText += val + "; ";
							// Add to mapping
							if (!textValueMappings.ContainsKey(key))
								textValueMappings[key] = new Dictionary<double, string>();
							if (!textValueMappings[key].ContainsKey(numVal))
								textValueMappings[key][numVal] = val;
						}
						else
						{
							dict[key] = -1;
							vectorizedValues.Add(-1);
						}
					}
					else
					{
						double numVal = item.ContainsKey(col.Column) ? Convert.ToDouble(item[col.Column]) : 0.0;
						dict[key] = numVal;
						vectorizedValues.Add(numVal);
					}
				}
				var mapping = new TextVectorMapping
				{
					OriginalText = originalText.TrimEnd(' ', ';'),
					VectorizedValues = vectorizedValues
				};
				dataModels.Add(new DataCollection(dict, mapping));
			}
			// Assign mapping to map
			int.TryParse(MapWidthTextBox.Text, out int width);
			int.TryParse(MapHeightTextBox.Text, out int height);
			int.TryParse(SeedTextBox.Text, out int seed);
			if (seed == 0)
			{
				seed = (int)DateTime.Now.Ticks;
				SeedTextBox.Text = seed.ToString();
			}

			_map = new Map(width: width < 0 ? 24 : width, height: height < 0 ? 18 : height, seed);
			_map.TextValueMappings = textValueMappings;

			_map.Initialize(dataModels, inputColumns);

			double.TryParse(LearningRateTextBox.Text, out double learningRate);
			if (learningRate <= 0)
			{
				learningRate = 0.03;
				LearningRateTextBox.Text = learningRate.ToString();
			}

			int.TryParse(IterationsTextBox.Text, out int iterations);
			if (iterations <= 0)
			{
				iterations = 100;
				IterationsTextBox.Text = iterations.ToString();
			}

			Train(_map, dataModels, learningRate, iterations);
		}

		private void Result_Click(object sender, RoutedEventArgs e)
		{
			// Set both the map and the checkbox state for the result
			_tcs.SetResult(_map);
			Close();
		}

		// Property to store the Show Data Reduction checkbox state
		public bool ShowDataReduction => ShowDataReductionCheckBox?.IsChecked == true;

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
