using SimpleMusicRhythm.Music;

namespace SimpleMusicRhythm.Rendering;

public static class Metrics
{
    public const double Half = 5;          // half the distance between staff lines
    public const double PadTop = 8;
    public const double SysH = 120;        // height of one line of music
    public const double StaffOffset = 50;  // staff top inside a line of music
    public const double StaffX = 14;
    public const double ClefW = 50;
    public const double TsW = 34;
    public const double Right = 14;
    public const double BarPadL = 12;
    public const double BarPadR = 8;

    // The rhythm staff is a single line, in the middle of where a five-line staff would be.
    public const double LineY = 20;          // below the staff top

    public static double YAt(double staffTop, int step) => staffTop + 40 - (step - Theory.E4) * Half;
}

// One drawable symbol. A note that crosses a bar line becomes several items.
public sealed class Item
{
    public Note? Note;
    public bool Virtual;        // a rest added only to fill the last bar
    public bool Rest;
    public int Step;
    public Dur Dur;
    public bool Dot;
    public int T;
    public bool First;
    public bool TieNext;
    public bool ShowAcc;
    public int Bar;
    public int Offset;          // ticks from the start of the bar

    public int Sys;
    public double StaffTop, X, W, Cx, Y;

    public bool Stressed;       // starts on the first (strong) beat of a bar
    public bool Beamed, StemUp;
    public double StemX, StemEnd;
}

// In free writing one unit per note; once divided into bars, one unit per bar.
public sealed class Unit
{
    public bool Measure;
    public int Number;
    public int Used;
    public List<Item> Items { get; } = new();
}

public sealed class Line
{
    public List<Unit> Units { get; } = new();
    public double Natural, Left, StaffTop, End;
}

public sealed class Barline
{
    public double X, StaffTop;
    public bool Final;
}

public sealed record BarNumber(double X, double StaffTop, int Number);

public sealed class Beam
{
    public required List<Item> Items;
    public bool Up;
    public double X0, X1, B, Slope;
    public double YAt(double x) => B + Slope * (x - X0);
}

public sealed class ScoreLayout
{
    public double Width, Height;
    public double SysH = Metrics.SysH;
    public List<Line> Lines { get; } = new();
    public List<Item> Items { get; } = new();
    public List<Barline> Barlines { get; } = new();
    public List<BarNumber> BarNumbers { get; } = new();
    public List<Beam> Beams { get; } = new();

    static double ItemWidth(Item it) => 14 + 10 * Math.Sqrt(it.T) + (it.Dot ? 6 : 0) + (it.ShowAcc ? 10 : 0);
    static double UnitWidth(Unit u) =>
        u.Items.Sum(ItemWidth) + (u.Measure ? Metrics.BarPadL + Metrics.BarPadR : 0);

    public static ScoreLayout Compute(Sheet sheet, double width)
    {
        var units = BuildUnits(sheet);
        var L = new ScoreLayout { Width = width };
        double right = width - Metrics.Right;

        double LeftOf(int sys) => Metrics.StaffX + Metrics.ClefW + (sheet.Barred && sys == 0 ? Metrics.TsW : 0);
        Line NewLine()
        {
            var ln = new Line { Left = LeftOf(L.Lines.Count) };
            L.Lines.Add(ln);
            return ln;
        }

        var line = NewLine();
        foreach (var u in units)
        {
            double uw = UnitWidth(u);
            if (line.Units.Count > 0 && line.Natural + uw > right - line.Left) line = NewLine();
            line.Units.Add(u);
            line.Natural += uw;
        }

        for (int s = 0; s < L.Lines.Count; s++)
        {
            var ln = L.Lines[s];
            double avail = right - ln.Left;
            bool isLast = s == L.Lines.Count - 1;
            // Full lines are stretched to the right edge; the last one keeps its natural spacing.
            double scale = ln.Natural > 0 && (!isLast || ln.Natural > avail) ? avail / ln.Natural : 1;
            double staffTop = Metrics.PadTop + s * L.SysH + Metrics.StaffOffset;
            ln.StaffTop = staffTop;
            double x = ln.Left;
            for (int ui = 0; ui < ln.Units.Count; ui++)
            {
                var u = ln.Units[ui];
                if (u.Measure)
                {
                    if (ui == 0) L.BarNumbers.Add(new BarNumber(Metrics.StaffX + 2, staffTop, u.Number));
                    x += Metrics.BarPadL * scale;
                }
                foreach (var it in u.Items)
                {
                    it.Sys = s;
                    it.StaffTop = staffTop;
                    it.X = x;
                    it.W = ItemWidth(it) * scale;
                    it.Cx = x + (it.ShowAcc ? 10 : 0) + 10;
                    it.Y = staffTop + Metrics.LineY;
                    L.Items.Add(it);
                    x += it.W;
                }
                if (u.Measure)
                {
                    x += Metrics.BarPadR * scale;
                    L.Barlines.Add(new Barline { X = x, StaffTop = staffTop });
                }
            }
            ln.End = x;
        }
        if (L.Barlines.Count > 0) L.Barlines[^1].Final = true;

        foreach (var g in BeamGroups(sheet, L.Items)) L.Beams.Add(LayoutBeam(g));

        L.Height = Metrics.PadTop + L.Lines.Count * L.SysH;
        return L;
    }

    /* ----------------------- Bars (this is where they happen) ----------------------- */

    static List<Unit> BuildUnits(Sheet sheet)
    {
        var units = new List<Unit>();
        if (sheet.Notes.Count == 0) return units;

        if (!sheet.Barred)
        {
            foreach (var n in sheet.Notes)
            {
                var u = new Unit();
                u.Items.Add(MakeItem(n, new NoteValue(n.Ticks, n.Dur, n.Dot), true, false));
                units.Add(u);
            }
            return units;
        }

        int barTicks = sheet.BarTicks;
        Unit NewBar()
        {
            var b = new Unit { Measure = true, Number = units.Count + 1 };
            units.Add(b);
            return b;
        }
        var bar = NewBar();

        foreach (var n in sheet.Notes)
        {
            int remaining = n.Ticks;
            bool first = true;
            while (remaining > 0)
            {
                if (bar.Used == barTicks) bar = NewBar();
                int take = Math.Min(barTicks - bar.Used, remaining);
                foreach (var part in Theory.Decompose(take))
                {
                    remaining -= part.Ticks;
                    var it = MakeItem(n, part, first, !n.Rest && remaining > 0);
                    it.Bar = bar.Number;
                    it.Offset = bar.Used;
                    it.Stressed = first && !n.Rest && bar.Used == 0;
                    bar.Items.Add(it);
                    bar.Used += part.Ticks;
                    first = false;
                }
            }
        }

        if (bar.Used < barTicks && sheet.FillLast)
        {
            // Shorter rests first, so the silence grows towards the end of the bar.
            var parts = Theory.Decompose(barTicks - bar.Used);
            parts.Reverse();
            foreach (var p in parts)
            {
                bar.Items.Add(new Item
                {
                    Virtual = true, Rest = true, Dur = p.Dur, Dot = p.Dot, T = p.Ticks,
                    First = true, Bar = bar.Number, Offset = bar.Used,
                });
                bar.Used += p.Ticks;
            }
        }
        return units;
    }

    static Item MakeItem(Note n, NoteValue part, bool first, bool tieNext) => new()
    {
        Note = n, Rest = n.Rest, Step = n.Step,
        Dur = part.Dur, Dot = part.Dot, T = part.Ticks,
        First = first, TieNext = tieNext, ShowAcc = false,
    };

    /* ----------------------------------- Beams ----------------------------------- */

    static bool Beamable(Item it) => !it.Rest && !it.Virtual && (it.Dur == Dur.E || it.Dur == Dur.S);

    // Free writing: every run of eighths/sixteenths is joined. Once divided
    // into bars, runs are also cut at bar lines and at each beat (for short
    // beats like an eighth, the whole bar is one group).
    static List<List<Item>> BeamGroups(Sheet sheet, List<Item> items)
    {
        int groupTicks = sheet.BeatTicks >= 8 ? sheet.BeatTicks : sheet.BarTicks;
        var groups = new List<List<Item>>();
        var cur = new List<Item>();
        void Flush()
        {
            if (cur.Count > 1) groups.Add(cur);
            cur = new List<Item>();
        }
        foreach (var it in items)
        {
            if (!Beamable(it)) { Flush(); continue; }
            if (cur.Count > 0)
            {
                var prev = cur[^1];
                bool split = prev.Sys != it.Sys || (sheet.Barred &&
                    (prev.Bar != it.Bar || prev.Offset / groupTicks != it.Offset / groupTicks));
                if (split) Flush();
            }
            cur.Add(it);
        }
        Flush();
        return groups;
    }

    // Decide the stem direction of a group and fit a gently sloped beam
    // that keeps every stem at least a normal stem length.
    static Beam LayoutBeam(List<Item> g)
    {
        const bool up = true;   // rhythm notation: stems always point up
        double StemX(Item it) => it.Cx + (up ? 5.4 : -5.4);
        var first = g[0];
        var last = g[^1];
        double x0 = StemX(first), x1 = StemX(last);
        double slope = x1 > x0 ? Math.Clamp((last.Y - first.Y) / (x1 - x0), -0.25, 0.25) * 0.5 : 0;
        double mid = first.StaffTop + 20;
        double b = up ? double.PositiveInfinity : double.NegativeInfinity;
        foreach (var it in g)
        {
            double dx = StemX(it) - x0;
            foreach (var v in new[] { (up ? it.Y - 31 : it.Y + 31) - slope * dx, mid - slope * dx })
                b = up ? Math.Min(b, v) : Math.Max(b, v);
        }
        var beam = new Beam { Items = g, Up = up, X0 = x0, X1 = x1, B = b, Slope = slope };
        foreach (var it in g)
        {
            it.Beamed = true;
            it.StemUp = up;
            it.StemX = StemX(it);
            it.StemEnd = beam.YAt(it.StemX);
        }
        return beam;
    }

    // Where the insertion cursor is drawn.
    public (int Sys, double X) CursorPosition(IReadOnlyList<Note> notes, int cursor)
    {
        if (cursor < notes.Count)
        {
            var it = Items.FirstOrDefault(i => i.Note == notes[cursor] && i.First);
            if (it != null) return (it.Sys, it.X + 1);
        }
        var last = Items.LastOrDefault(i => !i.Virtual);
        if (last == null) return (0, Lines[0].Left + 6);
        return (last.Sys, last.X + last.W - 2);
    }
}
