using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SimpleMusicRhythm.Export;
using SimpleMusicRhythm.Music;

namespace SimpleMusicRhythm;

public partial class App : Application
{
    // Command-line options used for checking the app without clicking through it:
    //   <file>.smrhythm       open that sheet (double-clicking a sheet file)
    //   --demo              open a built-in example instead of the saved sheet (nothing is saved)
    //   --barred            divide the sheet into bars
    //   --free              show the sheet before it is divided into bars
    //   --snapshot <png>    render the window to an image and quit
    //   --size <W>x<H>      window size (for Store screenshots)
    //   --pdf <file>        export the sheet as PDF and quit
    //   --midi <file>       export the sheet as MIDI and quit
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        string? ArgAfter(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        bool demo = args.Contains("--demo");
        bool automated = ArgAfter("--snapshot") != null || ArgAfter("--pdf") != null;

        // A sheet file passed on the command line: that's how double-clicking a .smrhythm file arrives.
        string? openPath = args.FirstOrDefault(a => !a.StartsWith("--") &&
            a.EndsWith(SheetStore.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(a));
        if (openPath != null) openPath = Path.GetFullPath(openPath);

        // Already open? Hand the sheet to that window instead of starting a second one.
        if (!demo && !automated && !SingleInstance.TryBecomeFirst() && SingleInstance.SendToRunning(openPath ?? ""))
        {
            Shutdown();
            return;
        }

        // Normally the window starts with the sheet from last time; automated runs never touch it.
        var sheet = demo ? DemoSheet.Create() : automated ? new Sheet() : SheetStore.Load();
        string? lastFile = demo || automated ? null : SheetStore.LoadLastFile();

        if (ArgAfter("--pdf") is { } pdf)
        {
            if (openPath != null) sheet = SheetStore.ReadFile(openPath);
            if (args.Contains("--barred")) sheet.Barred = true;
            PdfExporter.Export(sheet, pdf);
            Shutdown();
            return;
        }
        if (ArgAfter("--midi") is { } midi)
        {
            if (openPath != null) sheet = SheetStore.ReadFile(openPath);
            if (args.Contains("--barred")) sheet.Barred = true;
            MidiExporter.Export(sheet, midi);
            Shutdown();
            return;
        }
        if (args.Contains("--barred")) sheet.Barred = true;

        var window = new MainWindow(sheet, persist: !demo && !automated, lastFile);
        // Opening through the window asks to save unsaved work first, like the Open button.
        if (openPath != null) window.Loaded += (_, _) => window.OpenFile(openPath);
        if (!demo && !automated)
            SingleInstance.Listen(path => Dispatcher.BeginInvoke(() => window.BringForwardAndOpen(path)));
        if (args.Contains("--free")) window.Loaded += (_, _) => window.ShowFreeWriting();
        // Snapshots must not pick up a preview note from wherever the mouse happens to be.
        if (automated) window.Score.IsHitTestVisible = false;
        if (ArgAfter("--size") is { } size && size.Split('x') is [var sw, var sh] &&
            double.TryParse(sw, out var ww) && double.TryParse(sh, out var wh))
        {
            window.Width = ww;
            window.Height = wh;
        }
        if (ArgAfter("--snapshot") is { } png)
        {
            window.ContentRendered += async (_, _) =>
            {
                await Task.Delay(400);
                SaveSnapshot(window, png);
                Shutdown();
            };
        }
        window.Show();
    }

    static void SaveSnapshot(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        int w = (int)content.ActualWidth, h = (int)content.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

static class DemoSheet
{
    public static Sheet Create()
    {
        var sheet = new Sheet { Title = "Demo rhythm" };
        int id = 1;
        // Lengths: w h q e s, "." = dotted, "r" before a length = rest.
        foreach (var tok in "q q e e q  e. s e e q q  h r:q e e  q. e r:e e e e  w  s s s s q r:h".Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool rest = tok.StartsWith("r:");
            var len = rest ? tok[2..] : tok;
            var dur = Enum.Parse<Dur>(len[..1].ToUpperInvariant());
            sheet.Notes.Add(new Note { Id = id++, Rest = rest, Dur = dur, Dot = len.EndsWith('.') });
        }
        return sheet;
    }
}
