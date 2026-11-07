# SimpleMusic Rhythm

A rhythm editor for beginners, from Awkward Snail. It is a sister app of
[SimpleMusic](https://github.com/AwkwardSnail/SimpleMusic).

- Write a rhythm freely on a single-line rhythm staff. You don't need to think about bars.
- Choose a time signature. SimpleMusic Rhythm divides the rhythm into bars, ties notes that cross a bar line, and marks the strong first beat of each bar with an accent (>).
- Use the metronome on its own in any time signature, or let it click along while the rhythm plays.

The web version is in `web/`. It is a static site (`index.html`, `app.js`,
`styles.css`) with no build step and no network requests: open `web/index.html`
in a browser, or host the folder on any static host such as GitHub Pages. The
sheet is remembered in the browser's local storage, and PDF export uses the
browser's print window ("Save as PDF").

The Windows app is in `desktop/SimpleMusicRhythm` (WPF, .NET 10). Build and run it:

```
cd desktop/SimpleMusicRhythm
dotnet run
```

Store packaging is described in `desktop/SimpleMusicRhythm/Packaging/README.md`.
