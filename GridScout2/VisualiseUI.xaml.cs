using eve_parse_ui;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GridScout2
{
  /// <summary>
  /// Interaction logic for VisualiseUI.xaml
  /// </summary>
  public partial class VisualiseUI : Window
  {
    public VisualiseUI()
    {
      InitializeComponent();
    }

    public async Task VisualiseAsync(ParsedUserInterface root)
    {
      await drawAllChildrenAsync(root.UiTree, string.Empty);
    }

    private async Task drawAllChildrenAsync(UITreeNodeNoDisplayRegion node, string pathSoFar)
    {
      var thisNodeType = node.pythonObjectTypeName;
      var thisNodeName = node.GetNameFromDictEntries();

      var description = thisNodeType;
      if (thisNodeName != null)
      {
        description += " [" + thisNodeName + "]";
      }

      var newPath = pathSoFar + " > " + description;

      await drawNode(node, newPath);
      foreach (var item in node.Children ?? [])
      {
        await drawAllChildrenAsync(item, newPath);
      }
    }

    // < Frame BorderBrush = "Black" BorderThickness = "0.2" Width = "100" Height = "100" HorizontalAlignment = "Left" VerticalAlignment = "Top" Margin = "100,100,0,0" ></ Frame >
    private async Task drawNode(UITreeNodeNoDisplayRegion? node, string path)
    {

      if (node is UITreeNodeWithDisplayRegion uiTreeNodeWithDisplayRegion)
      {
        var region = uiTreeNodeWithDisplayRegion.TotalDisplayRegion;
        var margin = new Thickness(region.X, region.Y, 0, 0);

        var frame = new Frame
        {
          BorderBrush = new SolidColorBrush(Colors.Black),
          BorderThickness = new Thickness(0.2),
          HorizontalAlignment = HorizontalAlignment.Left,
          VerticalAlignment = VerticalAlignment.Top,
          Width = region.Width,
          Height = region.Height,
          Margin = margin,
          Tag = path,
        };
        frame.MouseEnter += EveRoot_MouseEnter;
        frame.MouseLeave += EveRoot_MouseLeave;

        EveRoot.Children.Add(frame);
        await Task.Delay(1);
      }
    }

    private void EveRoot_MouseLeave(object sender, MouseEventArgs e)
    {
      if (sender is Frame frame)
      {
        frame.BorderBrush = new SolidColorBrush(Colors.Black);
      }
    }

    private void EveRoot_MouseEnter(object sender, MouseEventArgs e)
    {
      if (sender is Frame frame)
      {
        frame.BorderBrush = new SolidColorBrush(Colors.Red);

        var path = frame.Tag as string;
        if (path != null)
        {
          // set the caption/title of the window
          this.Title = path;
        }
      }
    }

  }
}
