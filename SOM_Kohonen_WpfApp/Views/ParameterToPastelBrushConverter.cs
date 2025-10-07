using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SOM_Kohonen_WpfApp.Views
{
    public class ParameterToPastelBrushConverter : IValueConverter
    {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			string param = value as string;
			if (string.IsNullOrEmpty(param))
				return Brushes.White;

			int hash = param.GetHashCode();

			// Base RGB from hash
			int r = (hash & 0xFF);
			int g = (hash >> 8) & 0xFF;
			int b = (hash >> 16) & 0xFF;

			// Desaturate and lighten to create calm pastel tones
			r = 200 + r / 10;
			g = 200 + g / 10;
			b = 200 + b / 10;

			// Clamp to valid range 0–255
			r = Math.Min(255, Math.Max(0, r));
			g = Math.Min(255, Math.Max(0, g));
			b = Math.Min(255, Math.Max(0, b));

			Color pastel = Color.FromRgb((byte)r, (byte)g, (byte)b);
			return new SolidColorBrush(pastel);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
