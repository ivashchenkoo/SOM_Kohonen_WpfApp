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

                double latticeRadius = Math.Max(map.Width, map.Height) / 2;
                double timeConstant = iterations / Math.Log(latticeRadius);
                double learningRate = learningRateStart;
                int iteration = 0;
                while (iteration < iterations)
                {
                    Dispatcher.Invoke(() =>
                    {
                        LogListView.Items.Add($"Iteration: {iteration + 1}, LearningRate: {learningRate}");
                        LogScrollViewer.ScrollToBottom();
                        IterationLabel.Content = iteration + 1;
                    });

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
                    InputOption = item.ToLower() == "code" || item.ToLower() == "id" ? InputOption.Info : InputOption.Input
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
            Regex regex = new Regex("[0-9]+");
            e.Handled = !regex.IsMatch(e.Text);
        }

        private void Preview_TextInputDouble(object sender, TextCompositionEventArgs e)
        {
            Regex regex = new Regex(@"^[0-9]*(?:[\,\.][0-9]*)?$");
            e.Handled = !regex.IsMatch(e.Text);
        }

        private void Train_Click(object sender, RoutedEventArgs e)
        {
            LogListView.Items.Clear();

            // Initializing training data
            List<DataCollection> dataModels = new List<DataCollection>();
            foreach (var item in jsonData)
            {
                dataModels.Add(new DataCollection(item));
            }

            var columnsToRename = dataColumns.Where(x => x.Column != x.MainColumn).ToList();
            for (int i = 0; i < columnsToRename.Count; i++)
            {
                dataModels.ForEach(x => x.Where(y => y.Key == columnsToRename[i].MainColumn).ToList().ForEach(z => z.Key = columnsToRename[i].Column));
            }

            int.TryParse(MapWidthTextBox.Text, out int width);
            int.TryParse(MapHeightTextBox.Text, out int height);
            _map = new Map(width: width < 0 ? 24 : width, height: height < 0 ? 18 : height);
            _map.Initialize(dataModels, dataColumns.Select(x => x.Column).ToList());

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
            _tcs.SetResult(_map);
            Close();
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
