using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SOM_Kohonen_WpfApp.Models;

namespace SOM_Kohonen_WpfApp.Service
{
    public enum NormalizationMode { None, MinMax, ZScore }

    public sealed class ComparisonConfig
    {
        public string Dataset { get; set; }
        public string IdColumn { get; set; }
        public List<string> Features { get; set; } = new List<string>();
        public NormalizationMode Normalization { get; set; } = NormalizationMode.MinMax;
        public int Width { get; set; } = 24;
        public int Height { get; set; } = 18;
        public int Iterations { get; set; } = 100;
        public double LearningRate { get; set; } = 0.03;
        public double Radius { get; set; } = 12;
        public int[] Seeds { get; set; }
        public string SeedMode { get; set; }
        public int BaseSeed { get; set; }
        public int K { get; set; } = 4;
        public int KMeansSeed { get; set; } = 42;
        public int KMeansIterations { get; set; } = 100;
        public string Metric => "Euclidean";
        public string Neighborhood => "Gaussian, truncated at radius";
        public int KMeansInitializations => 1;
        public string TrainingOrder => "Seeded Fisher-Yates once; same order in every full-pass iteration";
        public string Schedule => "rate(t)=rate*exp(-t/iterations); radius(t)=radius*exp(-t/iterations*log(radius))";
        public string Implementation => "SOM comparison v1 / .NET Framework 4.7.2";

        public void Validate()
        {
            if (Features == null || Features.Count == 0 || Features.Distinct().Count() != Features.Count)
                throw new ArgumentException("Виберіть принаймні одну унікальну числову ознаку.");
            if (IdColumn != null && Features.Contains(IdColumn))
                throw new ArgumentException("Ідентифікатор не може бути ознакою навчання.");
            if (Width < 1 || Height < 1 || (long)Width * Height > int.MaxValue || Iterations < 1)
                throw new ArgumentException("Розміри карти та кількість ітерацій мають бути додатними.");
            if (!Finite(LearningRate) || LearningRate <= 0 || LearningRate > 1 || !Finite(Radius) || Radius < 1 || Radius > Math.Sqrt(double.MaxValue))
                throw new ArgumentException("Швидкість навчання: (0; 1], радіус: від 1, скінченне число.");
            if (Seeds == null || Seeds.Length == 0 || Seeds.Distinct().Count() != Seeds.Length)
                throw new ArgumentException("Список seed має бути непорожнім і без дублікатів.");
            if (K < 1 || K > (long)Width * Height || KMeansIterations < 1)
                throw new ArgumentException("k має бути від 1 до кількості нейронів; ітерації k-means — додатні.");
            if (!Enum.IsDefined(typeof(NormalizationMode), Normalization)) throw new ArgumentException("Невідома нормалізація.");
        }

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public static int[] GenerateSeeds(int baseSeed, int count)
        {
            if (count < 1 || count > 100000) throw new ArgumentException("Кількість пар: від 1 до 100000.");
            var random = new Random(baseSeed);
            var used = new HashSet<int>();
            var seeds = new List<int>();
            while (seeds.Count < count) { int seed = random.Next(); if (used.Add(seed)) seeds.Add(seed); }
            return seeds.ToArray();
        }

        public static int[] ParseSeeds(string text)
        {
            var parts = text.Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var values = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length == 0) throw new ArgumentException("Введіть список seed.");
            var duplicates = values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (duplicates.Length > 0) throw new ArgumentException("Повторені seed: " + string.Join(", ", duplicates));
            return values;
        }
    }

    public sealed class PreparedComparisonData
    {
        public string[] Ids { get; set; }
        public double[][] Original { get; set; }
        public List<DataCollection> Normalized { get; set; }
        public double[] Offsets { get; set; }
        public double[] Scales { get; set; }
        public string Fingerprint { get; set; }

        public static bool TryNumber(object value, out double number)
        {
            number = 0;
            return value != null && !(value is bool) && double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float, CultureInfo.InvariantCulture, out number) && ComparisonConfig.Finite(number);
        }

        public static PreparedComparisonData Create(List<Dictionary<string, object>> rows, ComparisonConfig config,
            CancellationToken cancellation = default(CancellationToken))
        {
            config.Validate();
            if (rows == null || rows.Count < 2) throw new ArgumentException("Потрібно щонайменше два записи.");
            var result = new PreparedComparisonData { Ids = new string[rows.Count], Original = new double[rows.Count][] };
            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < rows.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                object id;
                if (config.IdColumn != null && (!rows[i].TryGetValue(config.IdColumn, out id) || id == null))
                    throw new ArgumentException("Відсутній ID у рядку " + (i + 1));
                result.Ids[i] = config.IdColumn == null ? "row:" + (i + 1).ToString(CultureInfo.InvariantCulture)
                    : Convert.ToString(rows[i][config.IdColumn], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(result.Ids[i]) || !used.Add(result.Ids[i]))
                    throw new ArgumentException("Порожній або повторений ID у рядку " + (i + 1));
                result.Original[i] = new double[config.Features.Count];
                for (int j = 0; j < config.Features.Count; j++)
                {
                    object value; double number;
                    if (!rows[i].TryGetValue(config.Features[j], out value) || !TryNumber(value, out number))
                        throw new ArgumentException("Не скінченне число: рядок " + (i + 1) + ", " + config.Features[j]);
                    result.Original[i][j] = number;
                }
            }
            result.Offsets = new double[config.Features.Count];
            result.Scales = new double[config.Features.Count];
            for (int j = 0; j < config.Features.Count; j++)
            {
                cancellation.ThrowIfCancellationRequested();
                double[] column = result.Original.Select(r => r[j]).ToArray();
                double offset = 0, scale = 1;
                if (config.Normalization == NormalizationMode.MinMax) { offset = column.Min(); scale = column.Max() - offset; }
                if (config.Normalization == NormalizationMode.ZScore)
                {
                    offset = column.Average();
                    scale = Math.Sqrt(column.Average(v => (v - offset) * (v - offset)));
                }
                if (!ComparisonConfig.Finite(offset) || !ComparisonConfig.Finite(scale))
                    throw new ArgumentException("Завеликі значення ознаки: " + config.Features[j]);
                result.Offsets[j] = offset;
                result.Scales[j] = scale == 0 ? 1 : scale;
            }
            result.Normalized = result.Original.Select(row => new DataCollection(config.Features.Select((f, j) =>
                new { f, value = (row[j] - result.Offsets[j]) / result.Scales[j] }).ToDictionary(x => x.f, x => (object)x.value))).ToList();
            if (result.Normalized.Any(r => r.Any(v => !ComparisonConfig.Finite(v.GetDoubleValue()) || Math.Abs(v.GetDoubleValue()) > 1e150 / config.Features.Count)))
                throw new ArgumentException("Завеликі нормалізовані значення; змініть нормалізацію.");
            // Canonical JSON includes source row order, IDs, feature order and original values.
            string canonical = Newtonsoft.Json.JsonConvert.SerializeObject(new { config.Features, result.Ids, result.Original });
            using (var sha = SHA256.Create()) result.Fingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
            return result;
        }
    }
}
