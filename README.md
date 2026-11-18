# Nexus HATS Desktop

Windows and Mac installers for the latest [Nexus USB HATS packs](https://github.com/TR4UNEXUS/Nexus-USB-HATS/releases).

The compact layout is retained: choose an SD card, choose a pack, then use
**Install HATS**. The Nexus logo, blue-purple colours and matching artwork replace
the original branding. Options, Info and the always-open activity log stay in the
same places.

## Downloads and updates

- **Windows:** `Nexus-HATS-Desktop.x64.exe` is a standalone 64-bit app. No separate .NET installation is needed.
- **Mac:** `Nexus-HATS-Desktop-Universal.dmg` contains the app for Apple Silicon and Intel Macs on macOS 14 or later. Drag it to Applications.

The pack list reads the release GitHub marks as Latest in `TR4UNEXUS/Nexus-USB-HATS`.
New pack ZIP names and variants are discovered automatically. Drafts, prereleases,
installer/Formatter attachments and source archives are not offered as packs.
App updates are checked separately in `TR4UNEXUS/Nexus-HATS-Desktop`, using the
desktop-specific filenames above; Switch NSP releases are ignored.

## One protection file for both apps

Both builds embed **[shared/hats-exclusions.json](shared/hats-exclusions.json)**.
Both also compile the same protection, app-identification and pack-selection code
from `shared/`. There are no separate Windows and Mac copies of that list to maintain.

Before installation, both apps check `hats-exclusions.json` on the current default
branch of the Nexus USB HATS repository. If the file does not exist, the included
list is used. A network failure, invalid list or cancelled request stops before
backup or SD changes. Nexus USB indexes and Manager settings remain protected even
if the remote list is empty.

USB game mappings in `atmosphere/usb_content`, HOME badge data in
`atmosphere/usb_badges`, and the adjacent `usb_content-repair` journal/snapshots
and `usb_content-recovery-*` folders are always preserved. These protections
also apply when the online list is empty and when an installation rolls back.
Incoming packs cannot supply replacement mappings or repair data, including
on a fresh card. Both apps use the same rules and protection file.

**HOC is updateable.** Its `atmosphere/kips/hoc.kip` and
`atmosphere/contents/00FF0000636C6BFF` files can be replaced by a newer pack.
Known sysmodules and other modules identified from their program metadata update
as complete components, including resource files. Game title folders, mods,
cheats and unrecognised content remain protected. Only sysmodules included in the
selected pack are replaced; unrelated installed modules are retained.

Verified backups, recovery, archive validation and card identity checks are retained.
Backups default to `Desktop/NexusHATSBackups`. This project does not write to a card
merely by opening the app or running the test suite.

## Build

Install the .NET 10 SDK. Keep `Windows`, `macOS` and `shared` together.

```powershell
dotnet run --project Windows/source/Hats.Tests -c Release
dotnet publish Windows/source/Hats.WinUI -c Release -p:Platform=x64 -o outputs/Windows
```

The portable Mac tests and actual Avalonia layout can also be checked on Windows:

```sh
dotnet run --project macOS/source/Hats.Mac.Tests -c Release
dotnet run --project macOS/source/Hats.Mac.Tests -c Release -- --render
```

The root `.github/workflows/build-desktop.yml` builds Windows, tests and compiles
Mac on native Apple Silicon and Intel runners, creates a universal DMG, and opens
the packaged app on both architectures. It stores build artifacts and does not
publish a release automatically. Run **Build Nexus desktop installers** from Actions.

Mac packaging uses `lipo`, `codesign` and `hdiutil`, so producing and verifying the
DMG needs a Mac or the macOS workflow. The default build is ad-hoc signed; it is
not Apple Developer ID signed or notarised. See `macOS/INSTALL.txt`.

## Source and credits

Based on the supplied complete Magic Monkei HATS source archive: Windows 2.1.8
and macOS 2.3.0. The Mac disk-detection fix from the original project's 2.3.1 source
is included. The original native WinUI and Avalonia layouts, backup and recovery
workflows are retained. Copyright and third-party notices remain included.

The original archive also includes an older Python implementation. The current
desktop builds use C#. Help with testing and improving Nexus is welcome.
