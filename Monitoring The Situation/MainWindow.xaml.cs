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
        public Thumb? RightHandle { get; set; }
        public Thumb? BottomHandle { get; set; }
        public Thumb? CornerHandle { get; set; }

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
        private bool _cancelled;

        private enum DragMode { Right, Bottom, Corner}

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
            cell.Border.MouseEnter += (_, __) => { if (_dragging == null) ShowHandles(cell, true); };
            cell.Border.MouseLeave += (_, __) => { if (_dragging != cell) ShowHandles(cell, false); };

            _canvas.Children.Add(cell.Border);

            // Right handle ─────────────────────────────────────────────────────────
            cell.RightHandle = MakeHandle(Cursors.SizeWE);
            cell.BottomHandle = MakeHandle(Cursors.SizeNS);
            cell.CornerHandle = MakeHandle(Cursors.SizeNWSE);

            WireHandle(cell.RightHandle, cell, DragMode.Right);
            WireHandle(cell.BottomHandle, cell, DragMode.Bottom);
            WireHandle(cell.CornerHandle, cell, DragMode.Corner);

            _canvas.Children.Add(cell.RightHandle);
            _canvas.Children.Add(cell.BottomHandle);
            _canvas.Children.Add(cell.CornerHandle);

            _cells.Add(cell);
            return cell;
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
            handle.DragStarted += (_, args) =>
            {
                _dragging = cell;
                _dragMode = mode;
                _cancelled = false;

                // Record the absolute canvas position where the drag originated.
                // args.HorizontalOffset / VerticalOffset are offsets inside the Thumb,
                // so we reconstruct the right/bottom edge of the cell at drag start.
                _dragOriginX = ColX(cell.Col) + CellW(cell.ColSpan);   // right edge
                _dragOriginY = RowY(cell.Row) + CellH(cell.RowSpan);   // bottom edge

                _previewColSpan = cell.ColSpan;
                _previewRowSpan = cell.RowSpan;

                _preview.Visibility = Visibility.Visible;
                UpdatePreview(cell.Row, cell.Col, cell.RowSpan, cell.ColSpan);

                // Bring preview to the top
                BringPreviewToFront();
            };

            double totalDX = 0, totalDY = 0;

            handle.DragStarted += (_, __) => { totalDX = 0; totalDY = 0; };

            handle.DragDelta += (_, e) =>
            {
                if (_dragging != cell || _cancelled)
                    return;

                Point mouse = Mouse.GetPosition(_canvas);

                int newColSpan = cell.ColSpan;
                int newRowSpan = cell.RowSpan;

                if (mode == DragMode.Right || mode == DragMode.Corner)
                {
                    newColSpan = ColSpanFromRightEdge(cell.Col, mouse.X);
                }

                if (mode == DragMode.Bottom || mode == DragMode.Corner)
                {
                    newRowSpan = RowSpanFromBottomEdge(cell.Row, mouse.Y);
                }

                _previewColSpan = newColSpan;
                _previewRowSpan = newRowSpan;

                UpdatePreview(cell.Row, cell.Col, newRowSpan, newColSpan);
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
                    ApplyResize(cell, _previewRowSpan, _previewColSpan);
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
            ShowHandles(cell, false);
            _dragging = null;
            _cancelled = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Resize / merge
        // ─────────────────────────────────────────────────────────────────────
        private void ApplyResize(CellModel cell, int newRowSpan, int newColSpan)
        {
            if (newRowSpan == cell.RowSpan && newColSpan == cell.ColSpan) return;

            var newSlots = new HashSet<(int, int)>();
            for (int r = cell.Row; r < cell.Row + newRowSpan; r++)
                for (int c = cell.Col; c < cell.Col + newColSpan; c++)
                    newSlots.Add((r, c));

            var victims = _cells
                .Where(other => other != cell && other.Occupies().Any(s => newSlots.Contains(s)))
                .ToList();

            var allVictimSlots = new HashSet<(int, int)>(
                victims.SelectMany(v => v.Occupies()));

            foreach (var v in victims)
                RemoveCell(v);

            cell.RowSpan = newRowSpan;
            cell.ColSpan = newColSpan;
            UpdateLabel(cell);

            // Any slot inside a victim that is not occupied by expanded slot
            // Becomes a new 1x1 cell
            var expandedSlots = new HashSet<(int, int)>(cell.Occupies());

            foreach (var (r, c) in allVictimSlots)
            {
                if (!expandedSlots.Contains((r, c)))
                {
                    // Only create if no existing cell already covers this slot
                    bool alreadyCovered = _cells.Any(existing =>
                        existing.Occupies().Contains((r, c)));
                    if (!alreadyCovered)
                        AddCell(r, c, 1, 1);
                }
            }
        }

        private void RemoveCell(CellModel cell)
        {
            _canvas.Children.Remove(cell.Border);
            _canvas.Children.Remove(cell.RightHandle);
            _canvas.Children.Remove(cell.BottomHandle);
            _canvas.Children.Remove(cell.CornerHandle);
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

            // Right handle
            double rh = Math.Max(h * 0.40, 28);
            Canvas.SetLeft(cell.RightHandle!, x + w - HANDLE_HIT / 2.0);
            Canvas.SetTop(cell.RightHandle!, y + (h - rh) / 2.0);
            cell.RightHandle!.Width = HANDLE_HIT;
            cell.RightHandle!.Height = rh;

            // Bottom handle
            double bw = Math.Max(w * 0.40, 28);
            Canvas.SetLeft(cell.BottomHandle!, x + (w - bw) / 2.0);
            Canvas.SetTop(cell.BottomHandle!, y + h - HANDLE_HIT / 2.0);
            cell.BottomHandle!.Width = bw;
            cell.BottomHandle!.Height = HANDLE_HIT;

            // Corner handle
            double ch = HANDLE_HIT + 2;
            Canvas.SetLeft(cell.CornerHandle!, x + w - ch / 2.0);
            Canvas.SetTop(cell.CornerHandle!, y + h - ch / 2.0);
            cell.CornerHandle!.Width = ch;
            cell.CornerHandle!.Height = ch;
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
            if (_canvas.Children.Contains(_preview))
            {
                _canvas.Children.Remove(_preview);
                _canvas.Children.Add(_preview);
            }
        }

        private void ShowHandles(CellModel cell, bool show)
        {
            double op = show ? 1.0 : 0.0;
            if (cell.RightHandle != null) cell.RightHandle.Opacity = op;
            if (cell.BottomHandle != null) cell.BottomHandle.Opacity = op;
            if (cell.CornerHandle != null) cell.CornerHandle.Opacity = op;
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
            int idx = MainTabControl.SelectedIndex;
            var item = MainTabControl.SelectedItem as TabItem;
            if (item?.Content is Canvas c) _hosts.Remove(c);
            MainTabControl.Items.Remove(MainTabControl.SelectedItem);
            MainTabControl.SelectedIndex = Math.Max(idx - 1, 0);
        }

        private void GridCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Canvas c && _hosts.TryGetValue(c, out var host))
                host.Resize();
        }
    }
}