using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace NPlus.Controls;

/// <summary>
/// Editable hex editor (offset / hex bytes / ASCII columns) — a cross-platform
/// replacement for Be.Windows.Forms.HexBox. Supports byte editing in both the hex and
/// ASCII columns, insert/overwrite modes, delete, keyboard navigation, selection, and
/// copy. Raises <see cref="BytesChanged"/> on every modification for dirty tracking.
/// </summary>
public sealed class HexView : UserControl
{
    private readonly HexCanvas _canvas;
    private readonly ScrollViewer _scroller;

    public HexView(byte[] bytes, string? filePath)
    {
        FilePath = filePath;
        _canvas = new HexCanvas(bytes);
        _scroller = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _canvas.Scroller = _scroller;
        _scroller.GetObservable(ScrollViewer.OffsetProperty).Subscribe(new AnonObserver(() => _canvas.InvalidateVisual()));
        Content = _scroller;
    }

    public string? FilePath { get; set; }

    /// <summary>Current (possibly edited) bytes.</summary>
    public byte[] Bytes => _canvas.GetBytes();

    public bool ReadOnlyHex { get => _canvas.ReadOnly; set => _canvas.ReadOnly = value; }

    public event Action? BytesChanged
    {
        add => _canvas.BytesChanged += value;
        remove => _canvas.BytesChanged -= value;
    }

    public void SetColors(IBrush background, IBrush foreground)
    {
        Background = background;
        _canvas.SetColors(background, foreground);
    }

    public void SetFontSize(double size) => _canvas.SetFontSize(size);

    public void FocusEditor() => _canvas.Focus();

    private sealed class AnonObserver : IObserver<Vector>
    {
        private readonly Action _onNext;
        public AnonObserver(Action onNext) => _onNext = onNext;
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(Vector value) => _onNext();
    }
}

/// <summary>The scrollable, focusable rendering+input surface for <see cref="HexView"/>.</summary>
internal sealed class HexCanvas : Control
{
    private const int BytesPerLine = 16;
    private const int OffsetCols = 8;
    private const int Gap1 = 2;   // cols between offset and hex
    private const int Gap2 = 3;   // cols between hex and ascii
    private const double PadX = 8, PadY = 4;

    private readonly List<byte> _bytes;
    private double _fontSize = 13;
    private Typeface _typeface = new("Cascadia Mono,Consolas,DejaVu Sans Mono,monospace");
    private double _cw = 8, _lh = 16;

    private IBrush _bg = Brushes.White;
    private IBrush _fg = Brushes.Black;
    private IBrush _offsetBrush = Brushes.Gray;
    private readonly IBrush _selBrush = new SolidColorBrush(Color.FromArgb(90, 80, 140, 220));
    private readonly IBrush _caretBrush = new SolidColorBrush(Color.FromRgb(220, 70, 70));

    private int _caretByte;       // 0.._bytes.Count (Count = append position)
    private int _caretNibble;     // 0 = high, 1 = low (hex area only)
    private bool _asciiArea;      // caret in ASCII column?
    private bool _insertMode;     // false = overwrite (HexBox default)
    private int _selAnchor = -1;  // selection anchor byte, or -1

    public ScrollViewer? Scroller { get; set; }
    public bool ReadOnly { get; set; }
    public event Action? BytesChanged;

    public HexCanvas(byte[] bytes)
    {
        _bytes = new List<byte>(bytes);
        Focusable = true;
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
        Remeasure();
    }

    public byte[] GetBytes() => _bytes.ToArray();

    public void SetColors(IBrush background, IBrush foreground)
    {
        _bg = background;
        _fg = foreground;
        var c = (foreground as ISolidColorBrush)?.Color ?? Colors.Gray;
        _offsetBrush = new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
        InvalidateVisual();
    }

    public void SetFontSize(double size)
    {
        _fontSize = size;
        Remeasure();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Remeasure()
    {
        var ft = new FormattedText("0", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, _fg);
        _cw = ft.WidthIncludingTrailingWhitespace > 0 ? ft.WidthIncludingTrailingWhitespace : ft.Width;
        _lh = ft.Height > 0 ? ft.Height + 2 : _fontSize * 1.3;
    }

    private int LineCount => _bytes.Count / BytesPerLine + 1;
    private double HexX0 => PadX + (OffsetCols + Gap1) * _cw;
    private double AsciiX0 => HexX0 + (BytesPerLine * 3 + 1 + Gap2) * _cw;
    private double TotalWidth => AsciiX0 + BytesPerLine * _cw + PadX;

    protected override Size MeasureOverride(Size availableSize) =>
        new(TotalWidth, PadY * 2 + LineCount * _lh);

    private double ByteHexX(int col) => HexX0 + (col * 3 + (col >= 8 ? 1 : 0)) * _cw;
    private double AsciiX(int col) => AsciiX0 + col * _cw;

    public override void Render(DrawingContext context)
    {
        _ctx = context;
        context.FillRectangle(_bg, new Rect(Bounds.Size));

        double scrollY = Scroller?.Offset.Y ?? 0;
        double viewH = Scroller?.Viewport.Height ?? Bounds.Height;
        int first = Math.Max(0, (int)((scrollY - PadY) / _lh));
        int last = Math.Min(LineCount - 1, (int)((scrollY + viewH - PadY) / _lh) + 1);

        // Selection highlight.
        if (TryGetSelection(out int selStart, out int selEnd))
        {
            for (int i = selStart; i < selEnd; i++)
            {
                int line = i / BytesPerLine, col = i % BytesPerLine;
                if (line < first || line > last) continue;
                double y = PadY + line * _lh;
                context.FillRectangle(_selBrush, new Rect(ByteHexX(col), y, _cw * 2, _lh));
                context.FillRectangle(_selBrush, new Rect(AsciiX(col), y, _cw, _lh));
            }
        }

        for (int line = first; line <= last; line++)
        {
            double y = PadY + line * _lh;
            int lineStart = line * BytesPerLine;
            int lineLen = Math.Min(BytesPerLine, _bytes.Count - lineStart);
            if (lineLen < 0) lineLen = 0;

            DrawText((lineStart).ToString("X8"), PadX, y, _offsetBrush);

            var hex = new StringBuilder();
            var ascii = new StringBuilder();
            for (int i = 0; i < BytesPerLine; i++)
            {
                if (i == 8) hex.Append(' ');
                if (i < lineLen)
                {
                    byte b = _bytes[lineStart + i];
                    hex.Append(b.ToString("X2")).Append(' ');
                    ascii.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
                }
                else hex.Append("   ");
            }
            DrawText(hex.ToString(), HexX0, y, _fg);
            DrawText(ascii.ToString(), AsciiX0, y, _fg);
        }

        // Caret.
        if (IsFocused)
        {
            int cLine = _caretByte / BytesPerLine, cCol = _caretByte % BytesPerLine;
            double y = PadY + cLine * _lh;
            double x = _asciiArea ? AsciiX(cCol) : ByteHexX(cCol) + _caretNibble * _cw;
            context.DrawLine(new Pen(_caretBrush, 1.5), new Point(x, y), new Point(x, y + _lh));
        }
    }

    private void DrawText(string text, double x, double y, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _typeface, _fontSize, brush);
        context_DrawText(ft, x, y);
    }

    // Local helper to keep the DrawingContext reference scoped per Render call.
    private DrawingContext? _ctx;
    private void context_DrawText(FormattedText ft, double x, double y) => _ctx?.DrawText(ft, new Point(x, y));

    // ---- Selection ----

    private bool TryGetSelection(out int start, out int end)
    {
        if (_selAnchor < 0 || _selAnchor == _caretByte) { start = end = 0; return false; }
        start = Math.Min(_selAnchor, _caretByte);
        end = Math.Max(_selAnchor, _caretByte);
        return end > start;
    }

    // ---- Input ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        SetCaretFromPoint(p, extendSelection: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Equals(e.Pointer.Captured, this))
        {
            SetCaretFromPoint(e.GetPosition(this), extendSelection: true);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
    }

    private void SetCaretFromPoint(Point p, bool extendSelection)
    {
        int line = Math.Max(0, (int)((p.Y - PadY) / _lh));
        int col;
        if (p.X >= AsciiX0 - _cw / 2)
        {
            _asciiArea = true;
            col = (int)((p.X - AsciiX0) / _cw);
            _caretNibble = 0;
        }
        else
        {
            _asciiArea = false;
            double c = (p.X - HexX0) / _cw;
            if (c < 0) c = 0;
            if (c >= 24) c -= 1; // group gap after 8th byte
            col = (int)(c / 3);
            _caretNibble = 0;
        }
        col = Math.Clamp(col, 0, BytesPerLine - 1);
        int idx = Math.Clamp(line * BytesPerLine + col, 0, _bytes.Count);

        if (!extendSelection) _selAnchor = idx;
        else if (_selAnchor < 0) _selAnchor = _caretByte;
        _caretByte = idx;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (ReadOnly || string.IsNullOrEmpty(e.Text)) return;
        char ch = e.Text[0];

        if (!_asciiArea)
        {
            int val = HexVal(ch);
            if (val < 0) return;
            DeleteSelectionIfAny();
            if (_insertMode && _caretNibble == 0) { _bytes.Insert(Math.Min(_caretByte, _bytes.Count), 0); }
            else if (_caretByte >= _bytes.Count) { _bytes.Add(0); }
            byte b = _bytes[_caretByte];
            b = _caretNibble == 0 ? (byte)((b & 0x0F) | (val << 4)) : (byte)((b & 0xF0) | val);
            _bytes[_caretByte] = b;
            if (_caretNibble == 0) _caretNibble = 1;
            else { _caretNibble = 0; _caretByte++; }
            Changed();
        }
        else
        {
            if (ch > 0xFF) return;
            DeleteSelectionIfAny();
            if (_insertMode) _bytes.Insert(Math.Min(_caretByte, _bytes.Count), (byte)ch);
            else if (_caretByte >= _bytes.Count) _bytes.Add((byte)ch);
            else _bytes[_caretByte] = (byte)ch;
            _caretByte++;
            Changed();
        }
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (ctrl && e.Key == Key.C) { CopySelection(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.A) { _selAnchor = 0; _caretByte = _bytes.Count; InvalidateVisual(); e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Left: Move(-1, shift); break;
            case Key.Right: Move(1, shift); break;
            case Key.Up: MoveByte(-BytesPerLine, shift); break;
            case Key.Down: MoveByte(BytesPerLine, shift); break;
            case Key.Home: SetCaret(_caretByte - _caretByte % BytesPerLine, shift); break;
            case Key.End: SetCaret(Math.Min(_caretByte - _caretByte % BytesPerLine + BytesPerLine - 1, _bytes.Count), shift); break;
            case Key.PageUp: MoveByte(-VisibleLines() * BytesPerLine, shift); break;
            case Key.PageDown: MoveByte(VisibleLines() * BytesPerLine, shift); break;
            case Key.Tab: _asciiArea = !_asciiArea; _caretNibble = 0; InvalidateVisual(); break;
            case Key.Insert: _insertMode = !_insertMode; break;
            case Key.Back:
                if (ReadOnly) break;
                if (TryGetSelection(out _, out _)) DeleteSelectionIfAny();
                else if (_caretByte > 0) { _bytes.RemoveAt(_caretByte - 1); _caretByte--; }
                _caretNibble = 0; Changed(); break;
            case Key.Delete:
                if (ReadOnly) break;
                if (TryGetSelection(out _, out _)) DeleteSelectionIfAny();
                else if (_caretByte < _bytes.Count) _bytes.RemoveAt(_caretByte);
                _caretNibble = 0; Changed(); break;
            default: return;
        }
        e.Handled = true;
    }

    private static int HexVal(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private int VisibleLines() => Math.Max(1, (int)((Scroller?.Viewport.Height ?? Bounds.Height) / _lh));

    private void Move(int dir, bool shift)
    {
        if (_asciiArea) { SetCaret(_caretByte + dir, shift); return; }
        if (dir > 0)
        {
            if (_caretNibble == 0) _caretNibble = 1;
            else { _caretNibble = 0; SetCaret(_caretByte + 1, shift); return; }
        }
        else
        {
            if (_caretNibble == 1) _caretNibble = 0;
            else if (_caretByte > 0) { SetCaret(_caretByte - 1, shift); _caretNibble = 1; return; }
        }
        UpdateAnchor(shift);
        InvalidateVisual();
    }

    private void MoveByte(int delta, bool shift) => SetCaret(_caretByte + delta, shift);

    private void SetCaret(int idx, bool shift)
    {
        _caretByte = Math.Clamp(idx, 0, _bytes.Count);
        UpdateAnchor(shift);
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void UpdateAnchor(bool shift)
    {
        if (shift) { if (_selAnchor < 0) _selAnchor = _caretByte; }
        else _selAnchor = -1;
    }

    private void DeleteSelectionIfAny()
    {
        if (!TryGetSelection(out int s, out int end)) return;
        _bytes.RemoveRange(s, end - s);
        _caretByte = s;
        _selAnchor = -1;
        _caretNibble = 0;
    }

    private void Changed()
    {
        _caretByte = Math.Clamp(_caretByte, 0, _bytes.Count);
        BytesChanged?.Invoke();
        InvalidateMeasure();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void EnsureCaretVisible()
    {
        if (Scroller == null) return;
        double y = PadY + (_caretByte / BytesPerLine) * _lh;
        double top = Scroller.Offset.Y;
        double bottom = top + Scroller.Viewport.Height;
        if (y < top) Scroller.Offset = Scroller.Offset.WithY(y);
        else if (y + _lh > bottom) Scroller.Offset = Scroller.Offset.WithY(y + _lh - Scroller.Viewport.Height);
    }

    private async void CopySelection()
    {
        if (!TryGetSelection(out int s, out int end)) return;
        var sb = new StringBuilder();
        for (int i = s; i < end; i++) { if (i > s) sb.Append(' '); sb.Append(_bytes[i].ToString("X2")); }
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null) await clipboard.SetTextAsync(sb.ToString());
    }

}
