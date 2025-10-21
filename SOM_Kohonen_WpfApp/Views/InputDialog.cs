using System.Windows;
using System.Windows.Controls;

namespace SOM_Kohonen_WpfApp.Views
{
	public class InputDialog : Window
	{
		public string InputText { get; private set; }
		public InputDialog(string prompt, string title, string defaultValue = "")
		{
			Title = title;
			Width = 300;
			Height = 150;
			WindowStartupLocation = WindowStartupLocation.CenterOwner;
			ResizeMode = ResizeMode.NoResize;
			var stack = new StackPanel { Margin = new Thickness(10) };
			stack.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
			var textBox = new TextBox { Text = defaultValue };
			stack.Children.Add(textBox);
			var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
			var okBtn = new Button { Content = "OK", Width = 60, IsDefault = true };
			var cancelBtn = new Button { Content = "Cancel", Width = 60, IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
			okBtn.Click += (s, e) => { InputText = textBox.Text; DialogResult = true; };
			btnPanel.Children.Add(okBtn);
			btnPanel.Children.Add(cancelBtn);
			stack.Children.Add(btnPanel);
			Content = stack;
		}
	}
}
