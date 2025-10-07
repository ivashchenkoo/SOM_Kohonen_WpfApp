using System.Collections.Generic;
using System.Windows;

namespace SOM_Kohonen_WpfApp.Views
{
    public partial class TextMappingsWindow : Window
    {
        public TextMappingsWindow(List<TextMappingRow> rows)
        {
            InitializeComponent();
            MappingsDataGrid.ItemsSource = rows;
        }
    }
}

public class TextMappingRow
{
    public string Parameter { get; set; }
    public string OriginalText { get; set; }
    public double NumericValue { get; set; }
}
