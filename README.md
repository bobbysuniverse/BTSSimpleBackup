# BTS SimpleBackup

**A free backup program for Windows that copies your files to a USB drive every week, with no cloud, no account and no internet connection.**

Your files go only to a drive you own and hold in your hand. They are never uploaded anywhere, so there is no online account to be hacked and no company holding your data.

A gift to the community from [Bishop Technical Services LLC](https://bishoptechservices.com). Released under the MIT License.

> **Important notice:** BTS SimpleBackup is free software provided "as is", with no warranty of any kind, under the [MIT License](LICENSE). Bishop Technical Services LLC is not liable for lost, damaged or unrecoverable files. A backup only helps if it works, so open your backup drive once in a while and check that your files are there. For anything you can't replace, such as family photos, keep a second copy somewhere else.

---

## For everyone: how to use it (about 2 minutes)

1. **Buy a USB drive** that is bigger than the files you want to protect. A good rule is *at least twice* the space your files use. A portable external hard drive is a good choice for a whole computer.
2. **[Download `BTSSimpleBackup.exe`](https://github.com/bobbysuniverse/BTSSimpleBackup/releases/latest/download/BTSSimpleBackup.exe)** and double-click it. There is nothing to install.
   *(If Windows shows a blue "Windows protected your PC" box, click **More info**, then **Run anyway**. This appears for any new program that is not yet widely downloaded.)*
3. **Plug in your USB drive.**
4. In the program, follow the three steps:
   1. Choose your backup drive from the list (click **Refresh** if you don't see it).
   2. Tick what to back up. Documents, Pictures and Desktop are ticked for you.
   3. Choose the day and time.
5. Click **Save and turn on weekly backup**, then **Back up now** to make your first backup.

That's it. A **BTS SimpleBackup icon** is added to your desktop and Start menu so you can open the program again any time (for example to change the day, or to click **Back up now**). Every week your computer backs itself up. Keep the drive plugged in, or plug it in around backup time. If it is missing, you get a reminder message.

### Getting files back

Open the USB drive in File Explorer, then the **BTS SimpleBackup** folder, then the folder with your computer's name. Your files are there as normal files and folders. Copy back whatever you need. No special program is required.

### How long does the first backup take?

The **first backup is the slow one**, because it copies everything. After that, only new and changed files are copied, which usually takes a few minutes.

The speed depends mostly on your drive and the USB port you use:

| Drive | Typical speed | 30 GB of files takes about |
|---|---|---|
| Portable SSD or external hard drive in a blue USB 3 port | 50-150 MB/s | 5-10 minutes |
| Basic USB flash drive | 3-20 MB/s | 30 minutes to 3 hours |

(In our own test, a basic 32 GB flash drive copied about 28 GB in roughly two hours.)

You don't have to wait. The program runs in the background and you can keep using your computer. If you need to stop, click **Cancel backup**. Nothing is lost, and the next backup continues where it stopped. For a whole computer, a portable SSD or external hard drive is a much better choice than a small flash drive.

### Good habits

- **Unplug the drive and keep it somewhere safe** when you are not backing up. A drive that isn't plugged in can't be damaged by a virus, ransomware, a power surge or a spilled drink.
- Keep your drive in a different room (or building) from your computer if you can, in case of fire or theft.
- Once in a while, open the drive and check that you can see your files.
- Drives wear out. Plan to replace it every few years.

### If your antivirus warns you

A backup program copies thousands of files in a short time, which is also what ransomware does, so some antivirus programs (Trend Micro, Norton, McAfee and others) may block a brand-new backup program the first time. This is a false alarm. BTS SimpleBackup only *reads* your files and only *writes* to your backup drive.

If this happens, open your antivirus settings, find its **exceptions** (sometimes called "trusted programs" or "allow list") and add `BTSSimpleBackup.exe`. Then run the backup again. It continues where it stopped, and your original files were never touched.

---

## What it does and doesn't do

| It does | It never |
|---|---|
| Copy new and changed files to your drive | Deletes or changes anything on your computer |
| Keep **one** older copy of any file that changes (`_Previous versions` folder) | Deletes anything from the backup drive |
| Find your drive even if Windows changes its drive letter | Connects to the internet |
| Run on schedule, and catch up if the computer was off | Asks for an administrator password |
| Skip files that a program has locked and retry next time | Collects any information about you |
| Warn you if the drive is missing or too full | |

Because it never deletes from the drive, files you delete from your computer stay in the backup. That is intentional (it protects you from deleting something by accident), but it also means the drive slowly fills over the years.

**Not backed up:** Windows itself and installed programs. This program protects *your files* (documents, photos, and so on). It is not a full "system image".

**Skipped on purpose:** Visual Studio's temporary `.vs` cache folders, the Recycle Bin, Office's temporary `~$` lock files, `Thumbs.db` and `desktop.ini`. They are rebuilt automatically and are never worth restoring. Shortcut folders that only point somewhere else (symbolic links and junctions) are also skipped, because they can loop back on themselves. The folders they point to are backed up when you choose them.

**OneDrive:** if your Documents, Pictures or Desktop are kept in OneDrive, files that are stored *only online* must be downloaded to your computer before they can be copied. The program does this automatically, so the first backup can be slow and will use some disk space and internet bandwidth. The window tells you when a file is being downloaded.

**Files that are being changed:** a file that another program is writing to at the moment it is copied (a database or an open mailbox, for example) can be copied in an in-between state. The next backup fixes it. For those files, close the program first.

**Drive format:** Most drives work. If your drive is formatted FAT32 (common on small USB sticks), single files larger than 4 GB can't be stored; the program skips them and says so in the log.

---

## For technical users

- C# / .NET 10, Windows Forms, no third-party packages.
- Settings: `%AppData%\BTSSimpleBackup\settings.json`. Status: `status.json`. Logs: `logs\backup-YYYY-MM-DD.log` (kept 90 days).
- The schedule is a normal Task Scheduler task named **BTS SimpleBackup (weekly)**. It runs as the current user (least privilege), has *Start when available* on, and also fires at sign-in as a safety net (it does nothing if a backup succeeded within the last 5 days).
- The program copies itself to `%LocalAppData%\Programs\BTSSimpleBackup\` when you click Save, so the schedule keeps working if you delete the download. The first Save also creates a Start menu shortcut and a desktop shortcut (both point to that copy).
- The drive is recognized by volume serial number. The label is used only if no serial number could be read.
- Files are copied to `*.partial` first, then renamed, so an interrupted copy is never mistaken for a good file. Unchanged files are detected by size and modified time (2-second tolerance for FAT).
- Layout on the drive: `BTS SimpleBackup\<COMPUTERNAME>\<Folder>\...` and `BTS SimpleBackup\<COMPUTERNAME>\_Previous versions\<Folder>\...`.
- Command line: `BTSSimpleBackup.exe --scheduled` runs a headless backup (this is what the task runs).

### Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
```

Make the single-file download for users:

```
dotnet publish -c Release -r win-x64
```

The result is one self-contained `BTSSimpleBackup.exe` in `bin\Release\net10.0-windows\win-x64\publish\`.

### Uninstall

Open the program and click **Uninstall...**. It removes the weekly schedule, the desktop and Start menu icons, the installed copy and the program's own settings and logs. Your backup drive and the files on it are not touched, and nothing of yours on the computer is deleted. If you downloaded the program file, you can delete that too.

To do it by hand: delete the Task Scheduler task **BTS SimpleBackup (weekly)**, the two **BTS SimpleBackup** shortcuts, and the folders `%LocalAppData%\Programs\BTSSimpleBackup` and `%AppData%\BTSSimpleBackup`.

---

## Contributing

Bug reports and improvements are welcome. Keep the project's promises: no network access, no telemetry, never delete user data, and keep it simple enough for a non-technical person.

## License

MIT. See [LICENSE](LICENSE).
