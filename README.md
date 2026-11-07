# SimpleMusic Rhythm

A rhythm editor for beginners, from Awkward Snail. It is a sister app of
[SimpleMusic](https://github.com/AwkwardSnail/SimpleMusic).

- Write a rhythm freely on a single-line rhythm staff. You don't need to think about bars.
- Choose a time signature. SimpleMusic Rhythm divides the rhythm into bars, ties notes that cross a bar line, and marks the strong first beat of each bar with an accent (>).
- Use the metronome on its own in any time signature, or let it click along while the rhythm plays.

The Windows app is in `desktop/SimpleMusicRhythm` (WPF, .NET 10). Build and run it:

```
cd desktop/SimpleMusicRhythm
dotnet run
```

Store packaging is described in `desktop/SimpleMusicRhythm/Packaging/README.md`.
