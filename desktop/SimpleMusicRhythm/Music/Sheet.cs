using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleMusicRhythm.Music;

public sealed class Note
{
    public int Id { get; set; }
    public bool Rest { get; set; }
    public Dur Dur { get; set; } = Dur.Q;
    public bool Dot { get; set; }

    // A rhythm has no pitch: every note sits on the one line of the rhythm staff.
    [JsonIgnore] public int Step => Theory.B4;

    [JsonIgnore] public int Ticks => Theory.Ticks(Dur, Dot);

    public Note Clone() => (Note)MemberwiseClone();
}

// The whole document: the notes in order plus how to divide them into bars.
public sealed class Sheet
{
    public string Title { get; set; } = "";
    public List<Note> Notes { get; set; } = new();
    public bool Barred { get; set; }
    public int Beats { get; set; } = 4;
    public int BeatTicks { get; set; } = 8;
    public bool FillLast { get; set; } = true;
    public int Tempo { get; set; } = 90;

    [JsonIgnore] public int BarTicks => Beats * BeatTicks;
    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Untitled rhythm" : Title.Trim();

    // A dotted beat means compound time: 2 dotted quarters per bar -> 6/8.
    [JsonIgnore]
    public (int Num, int Den) TimeSignature =>
        BeatTicks % 3 == 0 ? (Beats * 3, 32 / (BeatTicks / 3)) : (Beats, 32 / BeatTicks);

    public void Sanitize()
    {
        Notes = Notes.Where(n => Enum.IsDefined(n.Dur)).ToList();
        foreach (var n in Notes)
        {
            if (n.Dur == Dur.S) n.Dot = false;
        }
        Beats = Math.Clamp(Beats, 1, 16);
        if (!Theory.BeatChoices.Contains(BeatTicks)) BeatTicks = 8;
        Tempo = Math.Clamp(Tempo, 40, 200);
        Title ??= "";
        if (Title.Length > 120) Title = Title[..120];
    }
}

// Remembers the sheet between runs (the app's own data folder).
public static class SheetStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleMusicRhythm");
    static string FilePath => Path.Combine(DataFolder, "sheet.json");
    static string LastFilePath => Path.Combine(DataFolder, "lastfile.txt");

    /* ---------------------- Sheet files (.smrhythm) ---------------------- */

    public const string FileExtension = ".smrhythm";
    public const string FileFilter = "SimpleMusic Rhythm sheet (*.smrhythm)|*.smrhythm";
    const int FormatVersion = 1;

    // What a .smrhythm file holds: a marker, a format version and the sheet.
    sealed class SheetFile
    {
        public string App { get; set; } = "SimpleMusicRhythm";
        public int FormatVersion { get; set; } = SheetStore.FormatVersion;
        public Sheet? Sheet { get; set; }
    }

    public static string ToFileJson(Sheet sheet) => JsonSerializer.Serialize(new SheetFile { Sheet = sheet }, Options);

    public static Sheet ReadFile(string path)
    {
        SheetFile? file;
        try { file = JsonSerializer.Deserialize<SheetFile>(File.ReadAllText(path), Options); }
        catch (JsonException) { throw new InvalidDataException("This file isn't a SimpleMusic Rhythm sheet, or it is damaged."); }
        if (file?.App != "SimpleMusicRhythm" || file.Sheet == null)
            throw new InvalidDataException("This file isn't a SimpleMusic Rhythm sheet.");
        if (file.FormatVersion > FormatVersion)
            throw new InvalidDataException("This sheet was made with a newer version of SimpleMusic Rhythm. Please update the app to open it.");
        file.Sheet.Sanitize();
        return file.Sheet;
    }

    // Writes to a temporary file first, so a failed save never damages the old file.
    public static void WriteFile(Sheet sheet, string path)
    {
        var tmp = path + ".saving";
        File.WriteAllText(tmp, ToFileJson(sheet));
        File.Move(tmp, path, overwrite: true);
    }

    // Which file the remembered sheet belongs to, so it reopens as that file.
    public static string? LoadLastFile()
    {
        try
        {
            var path = File.Exists(LastFilePath) ? File.ReadAllText(LastFilePath).Trim() : "";
            return path.Length > 0 && File.Exists(path) ? path : null;
        }
        catch (Exception) { return null; }
    }

    public static void SaveLastFile(string? path)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(LastFilePath, path ?? "");
        }
        catch (Exception) { /* only a convenience */ }
    }

    /* --------------------- Remembered sheet (autosave) --------------------- */

    public static Sheet Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var sheet = JsonSerializer.Deserialize<Sheet>(File.ReadAllText(FilePath), Options);
                if (sheet != null) { sheet.Sanitize(); return sheet; }
            }
        }
        catch (Exception) { /* a broken save just starts a fresh sheet */ }
        return new Sheet();
    }

    public static void Save(Sheet sheet)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(sheet, Options));
        }
        catch (Exception) { /* not being able to save must never stop the editor */ }
    }
}
