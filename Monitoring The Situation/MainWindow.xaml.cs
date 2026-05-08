using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Monitoring_The_Situation
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            int tabNumber = MainTabControl.Items.Count + 1;

            // Add new tab to top
            TabItem newTab = new TabItem
            {
                Header = $"Tab {tabNumber}",
                Style = (Style)FindResource("BrowserTabItemStyle")
            };

            // Create 3x3 grid for new window
            Grid grid = CreateDefaultGrid();
            newTab.Content = grid;

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl.Items.Count > 0)
            {
                int currentIndex = MainTabControl.SelectedIndex;
                MainTabControl.Items.Remove(MainTabControl.SelectedItem);
                MainTabControl.SelectedIndex = Math.Max(currentIndex - 1, 0);
            }
        }

        private Grid CreateDefaultGrid()
        {
            Grid grid = new Grid();

            // Rows
            for (int i = 0; i < 3; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition());
            }

            // Columns
            for (int i = 0; i < 3; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            // Cells
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    Border border = new Border
                    {
                        Style = (Style)FindResource("GridCellStyle")
                    };

                    TextBlock text = new TextBlock
                    {
                        Text = $"Cell ({row},{col})",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)FindResource("TextMutedBrush")
                    };

                    border.Child = text;

                    Grid.SetRow(border, row);
                    Grid.SetColumn(border, col);

                    grid.Children.Add(border);
                }
            }

            return grid;
        }
    }
}