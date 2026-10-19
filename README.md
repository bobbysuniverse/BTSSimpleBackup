# Simple USB Backup

**A free backup program for Windows that copies your files to a USB drive every week, with no cloud, no account and no internet connection.**

Your files go only to a drive you own and hold in your hand. They are never uploaded anywhere, so there is no online account to be hacked and no company holding your data.

A gift to the community from [Bishop Technical Services LLC](https://bishoptechservices.com). Released under the MIT License.

---

## For everyone: how to use it (about 2 minutes)

1. **Buy a USB drive** that is bigger than the files you want to protect. A good rule is *at least twice* the space your files use. A portable external hard drive is a good choice for a whole computer.
2. **Download `SimpleUsbBackup.exe`** and double-click it. There is nothing to install.
   *(If Windows shows a blue "Windows protected your PC" box, click **More info**, then **Run anyway**. This appears for any new program that is not yet widely downloaded.)*
3. **Plug in your USB drive.**
4. In the program, follow the three steps:
   1. Choose your backup drive from the list (click **Refresh** if you don't see it).
   2. Tick what to back up. Documents, Pictures and Desktop are ticked for you.
   3. Choose the day and time.
5. Click **Save and turn on weekly backup**, then **Back up now** to make your first backup.

That's it. Every week your computer backs itself up. Keep the drive plugged in, or plug it in around backup time. If it is missing, you get a reminder message.

### Getting files back

Open the USB drive in File Explorer, then the **Simple USB Backup** folder, then the folder with your computer's name. Your files are there as normal files and folders. Copy back whatever you need. No special program is required.

### Good habits

- **Unplug the drive and keep it somewhere safe** when you are not backing up. A drive that isn't plugged in can't be damaged by a virus, ransomware, a power surge or a spilled drink.
- Keep your drive in a different room (or building) from your computer if you can, in case of fire or theft.
- Once in a while, open the drive and check that you can see your files.
- Drives wear out. Plan to replace it every few years.

---

## What it does and doesn't do

| It does | It never |
|---|---|
| Copy new and changed files to your drive | Deletes or changes anything on your computer |
| Keep **one** older copy of any file that changes (`_Previous versions` folder) | Deletes anything from the backup drive |
| Find your drive even if Windows changes its drive letter | Connects to the internet |
| Run on schedule, and catch up if the computer was off | Asks for an administrator password |
| Skip files that are open in another program and retry next time | Collects any information about you |
| Warn you if the drive is missing or too full | |

Because it never deletes from the drive, files you delete from your computer stay in the backup. That is intentional (it protects you from deleting something by accident), but it also means the drive slowly fills over the years.

**Not backed up:** Windows itself and installed programs. This program protects *your files* (documents, photos, and so on). It is not a full "system image".

**Drive format:** Most drives work. If your drive is formatted FAT32 (common on small USB sticks), single files larger than 4 GB can't be stored; the program skips them and says so in the log.

---

## For technical users

- C# / .NET 10, Windows Forms, no third-party packages.
- Settings: `%AppData%\SimpleUsbBackup\settings.json`. Status: `status.json`. Logs: `logs\backup-YYYY-MM-DD.log` (kept 90 days).
- The schedule is a normal Task Scheduler task named **Simple USB Backup (weekly)**. It runs as the current user (least privilege), has *Start when available* on, and also fires at sign-in as a safety net (it does nothing if a backup succeeded within the last 5 days).
- The program copies itself to `%LocalAppData%\Programs\SimpleUsbBackup\` when you click Save, so the schedule keeps working if you delete the download.
- The drive is recognized by volume serial number, with the label as a fallback.
- Files are copied to `*.partial` first, then renamed, so an interrupted copy is never mistaken for a good file. Unchanged files are detected by size and modified time (2-second tolerance for FAT).
- Layout on the drive: `Simple USB Backup\<COMPUTERNAME>\<Folder>\...` and `Simple USB Backup\<COMPUTERNAME>\_Previous versions\<Folder>\...`.
- Command line: `SimpleUsbBackup.exe --scheduled` runs a headless backup (this is what the task runs).

### Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
```

Make the single-file download for users:

```
dotnet publish -c Release -r win-x64
```

The result is one self-contained `SimpleUsbBackup.exe` in `bin\Release\net10.0-windows\win-x64\publish\`.

### Uninstall

Turn off the schedule by opening the program, unticking *Back up automatically every week* and clicking Save. Then delete `%LocalAppData%\Programs\SimpleUsbBackup` and `%AppData%\SimpleUsbBackup`. Your backup drive is not touched.

---

## Contributing

Bug reports and improvements are welcome. Keep the project's promises: no network access, no telemetry, never delete user data, and keep it simple enough for a non-technical person.

## License

MIT. See [LICENSE](LICENSE).
