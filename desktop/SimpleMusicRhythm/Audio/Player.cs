using System.Diagnostics;
using System.Media;
using System.Windows.Threading;
using SimpleMusicRhythm.Music;

namespace SimpleMusicRhythm.Audio;

// A tiny synthesiser: renders the rhythm and the metronome to an in-memory WAV.
public static class Synth
{
    public const int Rate = 44100;
    const double NoteFrequency = 392;           // the rhythm is played on one soft note (G4)
    const double StrongClick = 1760, WeakClick = 1320;

    public static long At(double seconds) => (long)Math.Round(seconds * Rate);

    // A soft triangle-wave note, held for the note's length so long and short notes sound different.
    public static void AddNote(double[] samples, long start, double seconds, double vol)
    {
        const double attack = 0.008;
        double hold = Math.Max(0.02, seconds * 0.6);
        int n = (int)(seconds * Rate);
        double phase = 0, inc = NoteFrequency / Rate;
        for (int i = 0; i < n && start + i < samples.Length; i++)
        {
            double t = (double)i / Rate;
            double env = t < attack ? vol * t / attack
                : t < hold ? vol - vol * 0.5 * (t - attack) / (hold - attack)
                : seconds > hold ? vol * 0.5 * (1 - (t - hold) / (seconds - hold)) : 0;
            samples[start + i] += (4 * Math.Abs(phase - 0.5) - 1) * env;
            phase += inc;
            if (phase >= 1) phase -= 1;
        }
    }

    // A short, quickly fading click; the first beat of the bar is higher and louder.
    public static void AddClick(double[] samples, long start, bool strong)
    {
        double f = strong ? StrongClick : WeakClick, vol = strong ? 0.45 : 0.28;
        int n = (int)(0.045 * Rate);
        for (int i = 0; i < n && start + i < samples.Length; i++)
        {
            double t = (double)i / Rate;
            samples[start + i] += Math.Sin(2 * Math.PI * f * t) * vol * Math.Exp(-t / 0.009);
        }
    }

    public static byte[] Wav(double[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + dataBytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(dataBytes);
        foreach (var s in samples) w.Write((short)Math.Clamp(s * 32767, -32768, 32767));
        w.Flush();
        return ms.ToArray();
    }
}

// How the beats fall: everything in ticks (1 tick = a 32nd note).
public readonly record struct Meter(double SecondsPerTick, int BeatTicks, int BarTicks)
{
    public static Meter Of(Sheet s) => new(60.0 / s.Tempo / s.BeatTicks, s.BeatTicks, s.BarTicks);
}

public sealed class Player
{
    readonly SoundPlayer music = new();
    readonly SoundPlayer preview = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    readonly Stopwatch clock = new();
    readonly List<(int Id, double Start)> marks = new();
    double total;
    int? current;

    public bool IsPlaying { get; private set; }
    public bool IsMetronome { get; private set; }
    public event Action<int?>? NoteChanged;   // which note is sounding now (null = none)
    public event Action? Stopped;

    public Player() => timer.Tick += (_, _) => Tick();

    // Plays the rhythm. `startTick` is where the first note falls in the whole
    // rhythm, so the clicks and the strong beats line up with the bars.
    public void Play(IReadOnlyList<Note> notes, int startTick, Meter m, bool clicks)
    {
        Stop();
        if (notes.Count == 0) return;
        int endTick = startTick + notes.Sum(n => n.Ticks);
        // Let the clicks run on to the end of the beat the rhythm ends in.
        int clickEnd = clicks ? (endTick + m.BeatTicks - 1) / m.BeatTicks * m.BeatTicks : endTick;
        total = (clickEnd - startTick) * m.SecondsPerTick;
        var samples = new double[Synth.At(total) + Synth.Rate / 10];

        marks.Clear();
        int tick = startTick;
        foreach (var n in notes)
        {
            double t = (tick - startTick) * m.SecondsPerTick;
            marks.Add((n.Id, t));
            bool strong = tick % m.BarTicks == 0;
            if (!n.Rest) Synth.AddNote(samples, Synth.At(t), n.Ticks * m.SecondsPerTick * 0.92, strong ? 0.3 : 0.2);
            tick += n.Ticks;
        }
        if (clicks)
        {
            int first = (startTick + m.BeatTicks - 1) / m.BeatTicks * m.BeatTicks;
            for (int b = first; b < clickEnd; b += m.BeatTicks)
                Synth.AddClick(samples, Synth.At((b - startTick) * m.SecondsPerTick), b % m.BarTicks == 0);
        }
        Start(samples, loop: false);
    }

    // The metronome on its own: one bar of clicks, repeated until stopped.
    public void Metronome(Meter m)
    {
        Stop();
        var samples = new double[Synth.At(m.BarTicks * m.SecondsPerTick)];
        for (int b = 0; b < m.BarTicks; b += m.BeatTicks)
            Synth.AddClick(samples, Synth.At(b * m.SecondsPerTick), b == 0);
        marks.Clear();
        total = double.PositiveInfinity;
        IsMetronome = true;
        Start(samples, loop: true);
    }

    void Start(double[] samples, bool loop)
    {
        try
        {
            music.Stream = new MemoryStream(Synth.Wav(samples));
            music.Load();
            if (loop) music.PlayLooping(); else music.Play();
        }
        catch (Exception) { /* no audio device: still run the highlight */ }
        clock.Restart();
        IsPlaying = true;
        current = null;
        Tick();
        timer.Start();
    }

    void Tick()
    {
        double e = clock.Elapsed.TotalSeconds;
        if (e >= total) { Stop(); return; }
        int? id = null;
        foreach (var m in marks)
        {
            if (m.Start > e) break;
            id = m.Id;
        }
        if (id != current)
        {
            current = id;
            NoteChanged?.Invoke(id);
        }
    }

    public void Stop()
    {
        if (!IsPlaying) return;
        timer.Stop();
        music.Stop();
        IsPlaying = false;
        IsMetronome = false;
        current = null;
        NoteChanged?.Invoke(null);
        Stopped?.Invoke();
    }

    public void Preview(Note n)
    {
        if (IsPlaying || n.Rest) return;
        try
        {
            var samples = new double[Synth.At(0.25)];
            Synth.AddNote(samples, 0, 0.22, 0.18);
            preview.Stream = new MemoryStream(Synth.Wav(samples));
            preview.Play();
        }
        catch (Exception) { /* no audio device */ }
    }
}
