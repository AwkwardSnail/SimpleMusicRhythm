# Publishing Simple Music Rhythm to the Microsoft Store

Everything needed to build the Store package is in this folder:

| File | What it is |
|---|---|
| `make-assets.ps1` | Draws the icon (two beamed eighth notes) at every size (`Assets\`), plus `SimpleMusicRhythm.ico` for the .exe |
| `AppxManifest.template.xml` | The package description (name, icons, capabilities) |
| `build-msix.ps1` | Publishes the app and builds `..\..\dist\SimpleMusicRhythm_<version>.msixbundle` (x64 + ARM64) |

## 1. Things only you can do (Partner Center)

1. **Developer account.** Sign up at <https://partner.microsoft.com/dashboard> with your Microsoft account.
2. **Reserve the app name.** Apps and games → New product → MSIX or PWA app → enter **SimpleMusicRhythm**.
   Store names are unique; if it's taken, pick another (e.g. "SimpleMusicRhythm – Easy Sheet Music") and
   change `DisplayName` in `AppxManifest.template.xml` to match.
3. **Copy the identity values.** Product management → Product identity. You need:
   - `Package/Identity/Name` (e.g. `12345YourName.SimpleMusicRhythm`)
   - `Package/Identity/Publisher` (e.g. `CN=1A2B3C4D-....`)
   - `Package/Properties/PublisherDisplayName` (your publisher name)

## 2. Build the Store package

From this folder, in PowerShell:

```powershell
.\build-msix.ps1 -IdentityName "<Package/Identity/Name>" `
                 -Publisher "<Package/Identity/Publisher>" `
                 -PublisherDisplayName "<PublisherDisplayName>"
```

The result is `desktop\dist\SimpleMusicRhythm_1.0.0.0.msixbundle`. No signing needed: the Store signs it.
For updates, raise the version: `-Version 1.0.1.0` (the last number must stay 0).

## 3. Fill in the submission

- **Packages:** upload the `.msixbundle`.
- **Restricted capability `runFullTrust`:** Partner Center asks why. Answer:
  *"SimpleMusicRhythm is a WPF desktop application packaged with MSIX; runFullTrust is required for desktop apps."*
- **Store listing:** description (draft below), screenshots (not made yet; `--demo --snapshot <png> --size 1600x1000` renders the window)
  (better ones of your own songs are welcome), category **Music**, a few search terms.
- **Properties:** the app doesn't collect personal data or use the internet, so a privacy policy
  isn't required. If you add accounts, online features or analytics later, you'll need one.
- **Age ratings:** answer the questionnaire. There is no user-to-user content, so it should come out as rated for everyone.
- **Pricing and availability:** free or paid, and which markets.

### Draft listing text

> **Feel the beat first, count the bars later.** Simple Music Rhythm is a rhythm editor for beginners.
> Write a rhythm on a single line without worrying about bars, then choose a time signature and
> Simple Music Rhythm divides it into bars, ties notes that cross a bar line and marks the strong
> first beat of every bar.
>
> - Whole to sixteenth notes, dotted notes and rests, with automatic beaming
> - A metronome in any time signature, with a higher click on the first beat
> - Play your rhythm with the metronome clicking along
> - Export a clean PDF to print, or a MIDI file

## 4. Optional checks before submitting

- **Windows App Certification Kit** (the same tests the Store runs). Needs an administrator PowerShell:
  ```powershell
  & "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
      -appxpackagepath "..\..\dist\SimpleMusicRhythm_1.0.0.0.msixbundle" -reportoutputpath "$env:TEMP\wack.xml"
  ```
- **Install the package yourself** before uploading. Windows only installs signed packages, so
  either turn on *Developer Mode* (Settings → System → For developers) and register the unpacked
  folder:
  ```powershell
  Add-AppxPackage -Register "..\..\dist\layout-x64\AppxManifest.xml"
  ```
  or sign a test build with your own certificate (`-SignCert my.pfx -SignPassword ...`) and trust
  that certificate on this PC. Remove the test install afterwards from Settings → Apps.
