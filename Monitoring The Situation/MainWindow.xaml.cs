using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Monitoring_The_Situation
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Data model
    // ─────────────────────────────────────────────────────────────────────────
    public class CellModel
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int RowSpan { get; set; } = 1;   // How many rows a cell spans
        public int ColSpan { get; set; } = 1;   // How many cols a cell spans

        // Visual elements of this cell
        public Border? Border { get; set; }
        public TextBlock? Label { get; set; }

        public Thumb? TopHandle { get; set; }
        public Thumb? BottomHandle { get; set; }
        public Thumb? LeftHandle { get; set; }
        public Thumb? RightHandle { get; set; }
        public Thumb? BottomLeftHandle { get; set; }
        public Thumb? BottomRightHandle { get; set; }
        public Thumb? TopLeftHandle { get; set; }
        public Thumb? TopRightHandle { get; set; }

        public IEnumerable<(int r, int c)> Occupies()
        {
            for (int r = Row; r < Row + RowSpan; r++)
                for (int c = Col; c < Col + ColSpan; c++)
                    yield return (r, c);
        }
    }

    // ───────────────────────────────────────────────────────────────────────────
    //  ResizableGridHost  –  manages one Canvas as a resizable 3×3 grid
    // ───────────────────────────────────────────────────────────────────────────
    public class ResizableGridHost
    {
        // Constants ─────────────────────────────────────────────────────────────
        private const int COLS = 3;
        private const int ROWS = 3;
        private const double GAP = 8;    // px gap between cells
        private const double HANDLE_HIT = 10;   // Thumb hit-area size
        // How far the mouse must travel past a cell boundary before snapping
        // to the next slot (deadzone = 25 % of one cell width/height).
        private const double SNAP_RATIO = 0.5;

        // State ─────────────────────────────────────────────────────────────────
        private readonly Canvas _canvas;
        private readonly Window _window;
        private readonly List<CellModel> _cells = new();
        private Border _preview = null!;

        // Drag states ───────────────────────────────────────────────────────────
        private CellModel? _dragging;    // cell whose handle is dragged
        private DragMode _dragMode;
        private double _dragOriginX;
        private double _dragOriginY;
        private int _previewColSpan;
        private int _previewRowSpan;
        private int _previewCol;
        private int _previewRow;
        private bool _cancelled;

        private enum DragMode
        {
            Left,
            Right,
            Top,
            Bottom,

            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        // Constructor ────────────────────────────────────────────────────────────
        public ResizableGridHost(Canvas canvas, Window window)
        {
            _canvas = canvas;
            _window = window;

            // ESC cancels any in-progress drag
            _window.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && _dragging != null)
                    CancelDrag();
            };

            BuildDefaultGrid();
        }

        // ───────────────────────────────────────────────────────────────────────────
        //  Layout math
        // ───────────────────────────────────────────────────────────────────────────
        private double UnitW() => (_canvas.ActualWidth - GAP * (COLS + 1)) / COLS;
        private double UnitH() => (_canvas.ActualHeight - GAP * (ROWS + 1)) / ROWS;

        private double ColX(int c) => GAP + c * (UnitW() + GAP);
        private double RowY(int r) => GAP + r * (UnitH() + GAP);
        private double CellW(int span) => UnitW() * span + GAP * (span - 1);
        private double CellH(int span) => UnitH() * span + GAP * (span - 1);

        // Right edge of column c
        private double ColRight(int c) => ColX(c) + UnitW();
        // Bottom edge of row r
        private double RowBottom(int r) => RowY(r) + UnitH();

        // Return how many cols the cell should span
        private int ColSpanFromRightEdge(int startCol, double rightEdgePx)
        {
            int span = 1;
            for (int c = startCol + 1; c < COLS; c++)
            {
                // Snap point = left edge of column c  +  deadzone
                double snapPoint = ColX(c) + UnitW() * SNAP_RATIO;
                if (rightEdgePx >= snapPoint)
                    span = c - startCol + 1;
                else
                    break;
            }
            return Math.Max(1, Math.Min(span, COLS - startCol));
        }

        private int RowSpanFromBottomEdge(int startRow, double bottomEdgePx)
        {
            int span = 1;
            for (int r = startRow + 1; r < ROWS; r++)
            {
                double snapPoint = RowY(r) + UnitH() * SNAP_RATIO;
                if (bottomEdgePx >= snapPoint)
                    span = r - startRow + 1;
                else
                    break;
            }
            return Math.Max(1, Math.Min(span, ROWS - startRow));
        }
        private int ColFromLeftEdge(int fixedRightCol, double px)
        {
            for (int c = 0; c <= fixedRightCol; c++)
            {
                double snapPoint = ColX(c) + UnitW() * (1.0 - SNAP_RATIO);
                if (px <= snapPoint)
                    return c;
            }
            return fixedRightCol;
        }

        private int RowFromTopEdge(int fixedBottomRow, double py)
        {
            for (int r = 0; r <= fixedBottomRow; r++)
            {
                double snapPoint = RowY(r) + UnitH() * (1.0 - SNAP_RATIO);
                if (py <= snapPoint)
                    return r;
            }
            return fixedBottomRow;
        }

        // Build / Rebuild ─────────────────────────────────────────────────────────
        private void BuildDefaultGrid()
        {
            _canvas.Children.Clear();
            _cells.Clear();

            for (int r = 0; r < ROWS; r++)
                for (int c = 0; c < COLS; c++)
                    AddCell(r, c, 1, 1);

            // Preview overlay on drag start
            _preview = new Border
            {
                Background = (Brush)_window.FindResource("DragPreviewBrush"),
                CornerRadius = new CornerRadius(6),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _canvas.Children.Add(_preview);

            LayoutAll();
        }

        // ───────────────────────────────────────────────────────────────────────────
        //  Cell creation
        // ───────────────────────────────────────────────────────────────────────────
        private CellModel AddCell(int row, int col, int rowSpan, int colSpan)
        {
            var cell = new CellModel { Row = row, Col = col, RowSpan = rowSpan, ColSpan = colSpan };

            // Border
            cell.Border = new Border { Style = (Style)_window.FindResource("GridCellStyle") };

            // Label ───────────────────────────────────────────────────────────────
            cell.Label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)_window.FindResource("TextMutedBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };
            UpdateLabel(cell);
            cell.Border.Child = cell.Label;

            // Show / hide handles on hover
            //cell.Border.MouseEnter += (_, __) => { if (_dragging == null) ShowHandles(cell, true); };
            //cell.Border.MouseLeave += (_, __) => { if (_dragging != cell) ShowHandles(cell, false); };

            _canvas.Children.Add(cell.Border);

            // handles ─────────────────────────────────────────────────────────
            cell.TopHandle = MakeHandle(Cursors.SizeNS);
            cell.BottomHandle = MakeHandle(Cursors.SizeNS);
            cell.LeftHandle = MakeHandle(Cursors.SizeWE);
            cell.RightHandle = MakeHandle(Cursors.SizeWE);
            cell.BottomLeftHandle = MakeHandle(Cursors.SizeNESW);
            cell.BottomRightHandle = MakeHandle(Cursors.SizeNWSE);
            cell.TopLeftHandle = MakeHandle(Cursors.SizeNWSE);
            cell.TopRightHandle = MakeHandle(Cursors.SizeNESW);

            WireHandle(cell.TopHandle, cell, DragMode.Top);
            WireHandle(cell.BottomHandle, cell, DragMode.Bottom);
            WireHandle(cell.LeftHandle, cell, DragMode.Left);
            WireHandle(cell.RightHandle, cell, DragMode.Right);
            WireHandle(cell.BottomLeftHandle, cell, DragMode.BottomLeft);
            WireHandle(cell.BottomRightHandle, cell, DragMode.BottomRight);
            WireHandle(cell.TopLeftHandle, cell, DragMode.TopLeft);
            WireHandle(cell.TopRightHandle, cell, DragMode.TopRight);

            _canvas.Children.Add(cell.TopHandle);
            _canvas.Children.Add(cell.BottomHandle);
            _canvas.Children.Add(cell.LeftHandle);
            _canvas.Children.Add(cell.RightHandle);
            _canvas.Children.Add(cell.BottomLeftHandle);
            _canvas.Children.Add(cell.BottomRightHandle);
            _canvas.Children.Add(cell.TopLeftHandle);
            _canvas.Children.Add(cell.TopRightHandle);

            _cells.Add(cell);
            return cell;
        }

        private static IEnumerable<Thumb> AllHandles(CellModel cell)
        {
            yield return cell.TopHandle!;
            yield return cell.BottomHandle!;
            yield return cell.LeftHandle!;
            yield return cell.RightHandle!;
            yield return cell.TopLeftHandle!;
            yield return cell.TopRightHandle!;
            yield return cell.BottomLeftHandle!;
            yield return cell.BottomRightHandle!;
        }

        private Thumb MakeHandle(Cursor cursor)
        {
            // Build template: rounded Border
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            borderFactory.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });

            var template = new ControlTemplate(typeof(Thumb));
            template.VisualTree = borderFactory;

            return new Thumb
            {
                Cursor = cursor,
                Opacity = 0,
                Background = (Brush)_window.FindResource("HandleBrush"),
                Template = template
            };
        }

        // ───────────────────────────────────────────────────────────────────────────
        //  Drag wiring
        // ───────────────────────────────────────────────────────────────────────────
        private void WireHandle(Thumb handle, CellModel cell, DragMode mode)
        {
            handle.DragStarted += (_, __) =>
            {
                _dragging = cell;
                _dragMode = mode;
                _cancelled = false;
                _previewRow = cell.Row;
                _previewCol = cell.Col;
                _previewRowSpan = cell.RowSpan;
                _previewColSpan = cell.ColSpan;
                _preview.Visibility = Visibility.Visible;
                UpdatePreview(_previewRow, _previewCol, _previewRowSpan, _previewColSpan);
                BringPreviewToFront();
            };

            handle.DragDelta += (_, __) =>
            {
                if (_dragging != cell || _cancelled)
                    return;

                Point mouse = Mouse.GetPosition(_canvas);

                int fixedRightCol = cell.Col + cell.ColSpan - 1;
                int fixedBottomRow = cell.Row + cell.RowSpan - 1;

                int newRow = cell.Row;
                int newCol = cell.Col;
                int newRowSpan = cell.RowSpan;
                int newColSpan = cell.ColSpan;

                switch (mode)
                {
                    case DragMode.Right:
                        newColSpan = ColSpanFromRightEdge(cell.Col, mouse.X);
                        break;

                    case DragMode.Bottom:
                        newRowSpan = RowSpanFromBottomEdge(cell.Row, mouse.Y);
                        break;

                    case DragMode.BottomRight:
                        newColSpan = ColSpanFromRightEdge(cell.Col, mouse.X);
                        newRowSpan = RowSpanFromBottomEdge(cell.Row, mouse.Y);
                        break;

                    case DragMode.Left:
                        {
                            int newC = ColFromLeftEdge(fixedRightCol, mouse.X);
                            newCol = newC;
                            newColSpan = fixedRightCol - newC + 1;
                            break;
                        }

                    case DragMode.Top:
                        {
                            int newR = RowFromTopEdge(fixedBottomRow, mouse.Y);
                            newRow = newR;
                            newRowSpan = fixedBottomRow - newR + 1;
                            break;
                        }

                    case DragMode.TopLeft:
                        {
                            int newC = ColFromLeftEdge(fixedRightCol, mouse.X);
                            int newR = RowFromTopEdge(fixedBottomRow, mouse.Y);
                            newCol = newC;
                            newRow = newR;
                            newColSpan = fixedRightCol - newC + 1;
                            newRowSpan = fixedBottomRow - newR + 1;
                            break;
                        }

                    case DragMode.TopRight:
                        {
                            int newR = RowFromTopEdge(fixedBottomRow, mouse.Y);
                            newRow = newR;
                            newRowSpan = fixedBottomRow - newR + 1;
                            newColSpan = ColSpanFromRightEdge(cell.Col, mouse.X);
                            break;
                        }

                    case DragMode.BottomLeft:
                        {
                            int newC = ColFromLeftEdge(fixedRightCol, mouse.X);
                            newCol = newC;
                            newColSpan = fixedRightCol - newC + 1;
                            newRowSpan = RowSpanFromBottomEdge(cell.Row, mouse.Y);
                            break;
                        }
                }

                newCol = Math.Max(0, Math.Min(newCol, COLS - 1));
                newRow = Math.Max(0, Math.Min(newRow, ROWS - 1));
                newColSpan = Math.Max(1, Math.Min(newColSpan, COLS - newCol));
                newRowSpan = Math.Max(1, Math.Min(newRowSpan, ROWS - newRow));

                _previewRow = newRow;
                _previewCol = newCol;
                _previewRowSpan = newRowSpan;
                _previewColSpan = newColSpan;
                UpdatePreview(newRow, newCol, newRowSpan, newColSpan);
            };

            handle.DragCompleted += (_, __) =>
            {
                if (_cancelled)
                {
                    FinishDrag(cell);
                    return;
                }

                if (_dragging == cell)
                {
                    _preview.Visibility = Visibility.Collapsed;
                    ApplyResize(cell, _previewRow, _previewCol, _previewRowSpan, _previewColSpan);
                    FinishDrag(cell);
                    LayoutAll();
                }
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cancel / finish helpers
        // ─────────────────────────────────────────────────────────────────────

        private void CancelDrag()
        {
            if (_dragging == null) return;
            _cancelled = true;
            _preview.Visibility = Visibility.Collapsed;
            // Restore preview to original cell size so it disappears cleanly
            UpdatePreview(_dragging.Row, _dragging.Col, _dragging.RowSpan, _dragging.ColSpan);
        }

        private void FinishDrag(CellModel cell)
        {
            //ShowHandles(cell, false);
            _dragging = null;
            _cancelled = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Resize / merge
        // ─────────────────────────────────────────────────────────────────────
        private void ApplyResize(CellModel cell, int newRow, int newCol, int newRowSpan, int newColSpan)
        {
            bool sameGeometry = newRow == cell.Row && newCol == cell.Col
                             && newRowSpan == cell.RowSpan && newColSpan == cell.ColSpan;
            if (sameGeometry) return;

            var newSlots = new HashSet<(int, int)>();
            for (int r = newRow; r < newRow + newRowSpan; r++)
                for (int c = newCol; c < newCol + newColSpan; c++)
                    newSlots.Add((r, c));

            var victims = _cells
                .Where(other => other != cell && other.Occupies().Any(s => newSlots.Contains(s)))
                .ToList();

            var allVictimSlots = new HashSet<(int, int)>(victims.SelectMany(v => v.Occupies()));

            foreach (var v in victims)
                RemoveCell(v);

            cell.Row = newRow;
            cell.Col = newCol;
            cell.RowSpan = newRowSpan;
            cell.ColSpan = newColSpan;
            UpdateLabel(cell);

            // Re-fill any victim slots that are NOT covered by the resized cell
            var expandedSlots = new HashSet<(int, int)>(cell.Occupies());
            foreach (var (r, c) in allVictimSlots)
            {
                if (!expandedSlots.Contains((r, c)))
                {
                    bool covered = _cells.Any(existing => existing.Occupies().Contains((r, c)));
                    if (!covered) AddCell(r, c, 1, 1);
                }
            }
            FillEmptySlots();
        }

        private void FillEmptySlots()
        {
            for (int r = 0; r < ROWS; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    bool covered = _cells.Any(cell => cell.Occupies().Contains((r, c)));
                    if (!covered) AddCell(r, c, 1, 1);
                }
            }
        }

        private void RemoveCell(CellModel cell)
        {
            _canvas.Children.Remove(cell.Border);
            foreach (var h in AllHandles(cell)) _canvas.Children.Remove(h);
            _cells.Remove(cell);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Layout
        // ─────────────────────────────────────────────────────────────────────
        public void LayoutAll()
        {
            foreach (var cell in _cells)
                LayoutCell(cell);
            BringPreviewToFront();
        }

        private void LayoutCell(CellModel cell)
        {
            double x = ColX(cell.Col);
            double y = RowY(cell.Row);
            double w = CellW(cell.ColSpan);
            double h = CellH(cell.RowSpan);

            Canvas.SetLeft(cell.Border!, x);
            Canvas.SetTop(cell.Border!, y);
            cell.Border!.Width = w;
            cell.Border!.Height = h;

            double vhh = Math.Max(h, 28);   // vertical-handle height
            double hhw = Math.Max(w, 28);   // horizontal-handle width
            double cs = HANDLE_HIT + 2;            // corner size

            // Edges
            Place(cell.LeftHandle!, x - HANDLE_HIT / 2, y + (h - vhh) / 2, HANDLE_HIT, vhh);
            Place(cell.RightHandle!, x + w - HANDLE_HIT / 2, y + (h - vhh) / 2, HANDLE_HIT, vhh);
            Place(cell.TopHandle!, x + (w - hhw) / 2, y - HANDLE_HIT / 2, hhw, HANDLE_HIT);
            Place(cell.BottomHandle!, x + (w - hhw) / 2, y + h - HANDLE_HIT / 2, hhw, HANDLE_HIT);

            // Corners
            Place(cell.TopLeftHandle!, x - cs / 2, y - cs / 2, cs, cs);
            Place(cell.TopRightHandle!, x + w - cs / 2, y - cs / 2, cs, cs);
            Place(cell.BottomLeftHandle!, x - cs / 2, y + h - cs / 2, cs, cs);
            Place(cell.BottomRightHandle!, x + w - cs / 2, y + h - cs / 2, cs, cs);
        }

        private static void Place(UIElement el, double left, double top, double w, double h)
        {
            Canvas.SetLeft(el, left);
            Canvas.SetTop(el, top);
            ((FrameworkElement)el).Width = w;
            ((FrameworkElement)el).Height = h;
        }

        private void UpdatePreview(int row, int col, int rowSpan, int colSpan)
        {
            Canvas.SetLeft(_preview, ColX(col));
            Canvas.SetTop(_preview, RowY(row));
            _preview.Width = CellW(colSpan);
            _preview.Height = CellH(rowSpan);
        }

        private void BringPreviewToFront()
        {
            if (!_canvas.Children.Contains(_preview)) return;
            _canvas.Children.Remove(_preview);
            _canvas.Children.Add(_preview);
        }

        private void UpdateLabel(CellModel cell)
        {
            if (cell.Label == null) return;
            cell.Label.Text = (cell.RowSpan == 1 && cell.ColSpan == 1)
                ? $"Cell ({cell.Row},{cell.Col})"
                : $"({cell.Row},{cell.Col})  {cell.RowSpan}×{cell.ColSpan}";
        }

        // ── Public API ────────────────────────────────────────────────────────
        public void Resize() => LayoutAll();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MainWindow
    // ─────────────────────────────────────────────────────────────────────────
    public partial class MainWindow : Window
    {
        private readonly Dictionary<Canvas, ResizableGridHost> _hosts = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, __) => RegisterCanvas(InitialGridCanvas);
        }
        private void RegisterCanvas(Canvas canvas)
        {
            if (!_hosts.ContainsKey(canvas))
                _hosts[canvas] = new ResizableGridHost(canvas, this);
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            int n = MainTabControl.Items.Count + 1;
            var canvas = new Canvas { Background = (Brush)FindResource("BackgroundBrush") };
            canvas.SizeChanged += GridCanvas_SizeChanged;

            var tab = new TabItem
            {
                Header = $"Tab {n}",
                Style = (Style)FindResource("BrowserTabItemStyle"),
                Content = canvas
            };
            MainTabControl.Items.Add(tab);
            MainTabControl.SelectedItem = tab;

            canvas.Loaded += (_, __) => RegisterCanvas(canvas);
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl.Items.Count <= 1) return;
            DependencyObject? current = sender as DependencyObject;
            TabItem? tabToClose = null;
            while (current != null)
            {
                if (current is TabItem ti)
                {
                    tabToClose = ti;
                    break;
                }
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            if (tabToClose == null) return;

            int idx = MainTabControl.Items.IndexOf(tabToClose);
            if (tabToClose.Content is Canvas c) _hosts.Remove(c);
            MainTabControl.Items.Remove(tabToClose);
            MainTabControl.SelectedIndex = Math.Max(idx - 1, 0);
        }

        private void GridCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Canvas c && _hosts.TryGetValue(c, out var host))
                host.Resize();
        }
    }
}

// Still need to fix bug where if tab 1 is closed while tab 2 is open, adding another tab is still named tab 2 (it should be tab 3)