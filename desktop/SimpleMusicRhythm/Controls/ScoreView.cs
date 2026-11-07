using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SimpleMusicRhythm.Music;
using SimpleMusicRhythm.Rendering;

namespace SimpleMusicRhythm.Controls;

// The score: draws the sheet and turns clicks, drags and typing into edits.
// Clicking on the line places a note (or a rest); clicking a note selects it.
public sealed class ScoreView : FrameworkElement
{
    static readonly Dictionary<Role, Brush> Brushes = new()
    {
        [Role.Ink] = B("#1D1B22"),
        [Role.Selected] = B("#2F5BD3"),
        [Role.Playing] = B("#E2662B"),
        [Role.Faint] = B("#A8A3AE"),
        [Role.Staff] = B("#8F8A96"),
        [Role.Muted] = B("#6B6774"),
        [Role.Caret] = B("#2F5BD3"),
        [Role.Ghost] = B("#2F5BD3"),
        [Role.GhostLabelBg] = B("#E6EDFF"),
        [Role.Placeholder] = B("#C9C4CE"),
    };
    static readonly Dictionary<(Role, double), Pen> Pens = new();

    static Brush B(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    static Pen PenFor(Role r, double th)
    {
        if (!Pens.TryGetValue((r, th), out var p))
        {
            p = new Pen(Brushes[r], th);
            p.Freeze();
            Pens[(r, th)] = p;
        }
        return p;
    }

    Editor? editor;
    ScoreLayout? layout;
    Point? ghost;
    bool caretOn = true;
    List<DrawOp>? cachedOps;
    (ScoreLayout?, int?, int?) cacheKey;

    public ScoreView()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Cross;
        var blink = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        blink.Tick += (_, _) => { caretOn = !caretOn; InvalidateVisual(); };
        blink.Start();
    }

    public Editor? Editor
    {
        get => editor;
        set { editor = value; Refresh(); }
    }

    public void Refresh()
    {
        caretOn = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 900 : Math.Max(320, available.Width);
        if (editor == null) return new Size(w, Metrics.SysH);
        layout = ScoreLayout.Compute(editor.Sheet, w);
        return new Size(w, layout.Height);
    }


    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(RenderSize));
        if (editor == null || layout == null) return;

        // The score only needs rebuilding when the layout, selection, playing note or
        // lyric line being typed changes; the blinking cursors and hover preview go on top.
        var key = (layout, editor.SelectedId, editor.PlayingId);
        if (cachedOps == null || !cacheKey.Equals(key))
        {
            cachedOps = ScoreRenderer.Render(editor.Sheet, layout, new ScoreRenderOptions
            {
                SelectedId = editor.SelectedId, PlayingId = editor.PlayingId,
            });
            cacheKey = key;
        }
        Draw(dc, cachedOps);

        if (caretOn && editor.SelectedId == null && editor.PlayingId == null)
        {
            var c = layout.CursorPosition(editor.Sheet.Notes, editor.Cursor);
            double t = layout.Lines[c.Sys].StaffTop;
            dc.DrawLine(PenFor(Role.Caret, 2), new Point(c.X, t - 8), new Point(c.X, t + 48));
        }

        if (ghost is { } g && editor.PlayingId == null)
        {
            dc.PushOpacity(0.45);
            Draw(dc, GhostOps(g));
            dc.Pop();
        }
    }

    static void Draw(DrawingContext dc, List<DrawOp> ops)
    {
        foreach (var op in ops)
        {
            dc.DrawGeometry(op.Fill is Role f ? Brushes[f] : null,
                op.Stroke is Role s ? PenFor(s, op.Thickness) : null, op.Geometry);
        }
    }

    /* ------------------------------ Hit testing ------------------------------ */

    int SystemAt(double y) => Math.Clamp((int)Math.Floor((y - Metrics.PadTop) / layout!.SysH), 0, layout.Lines.Count - 1);


    Item? HitItem(Point p)
    {
        foreach (var it in layout!.Items)
        {
            if (it.Virtual || it.Note == null) continue;
            // A note or rest: its head and the stem above it.
            if (Math.Abs(p.X - it.Cx) <= 10 && p.Y >= it.Y - 36 && p.Y <= it.Y + 14) return it;
        }
        return null;
    }

    // Where a click should insert: before the first note that starts to the right of it.
    int InsertIndexAt(int sys, double x)
    {
        foreach (var it in layout!.Items)
        {
            if (it.Note == null || !it.First) continue;
            if (it.Sys > sys || (it.Sys == sys && it.Cx > x)) return editor!.Sheet.Notes.IndexOf(it.Note);
        }
        return editor!.Sheet.Notes.Count;
    }

    List<DrawOp> GhostOps(Point p)
    {
        var ops = new List<DrawOp>();
        int sys = SystemAt(p.Y);
        var ln = layout!.Lines[sys];
        double t = ln.StaffTop;
        double x = Math.Max(p.X, ln.Left + 8);
        bool dot = editor!.Dot && editor.Dur != Dur.S;
        if (editor.RestMode)
        {
            ScoreRenderer.RestOps(editor.Dur, dot, x, t, Role.Ghost, ops);
            return ops;
        }
        var it = new Item { Cx = x, Y = t + Metrics.LineY, StaffTop = t, Step = Theory.B4, Dur = editor.Dur, Dot = dot };
        ScoreRenderer.NoteOps(it, Role.Ghost, ops);
        return ops;
    }

    /* --------------------------------- Mouse --------------------------------- */

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (editor == null || layout == null) return;
        Focus();
        var p = e.GetPosition(this);
        e.Handled = true;

        if (HitItem(p)?.Note is { } note)
        {
            editor.Select(note);
            return;
        }
        editor.InsertNote(InsertIndexAt(SystemAt(p.Y), p.X), editor.RestMode);
        ghost = p;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (editor == null || layout == null) return;
        var p = e.GetPosition(this);
        var hit = HitItem(p);
        Cursor = hit == null ? Cursors.Cross : Cursors.Hand;
        ghost = hit == null ? p : null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        ghost = null;
        InvalidateVisual();
    }
}
