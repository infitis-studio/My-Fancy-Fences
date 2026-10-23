# Windows installer

Build with .NET 10 SDK and Inno Setup 6.7 or newer:

```powershell
./installer/Build-Installer.ps1 -CompilerPath 'C:/Program Files (x86)/Inno Setup 6/ISCC.exe'
```

The resulting `new-build/My-Fancy-Fences-v<version>-Setup-win-x64.exe` (the unpacked application is written next to it to `new-build/My Fancy Fences/`) defaults to installation for the current user only into `%LOCALAPPDATA%\Programs\My Fancy Fences`, without administrator approval, so updates install silently with no Windows prompt. The first Setup page lets the user choose installation for all users (`C:\Program Files\My Fancy Fences`, with administrator approval); the `/ALLUSERS` and `/CURRENTUSER` command-line options select the mode directly. Existing installations keep their selected directory and install mode during upgrades. Setup includes the application files, Start menu shortcut, and uninstaller. It preserves application settings on uninstall. Desktop shortcut and autostart are selectable; existing autostart is preserved by default, and desktop shortcut selection is remembered for upgrades. Autostart applies to the account running Setup; it uses the same registry value as the application's tray menu.

Setup detects stable .NET 10 Desktop Runtime x64 installations through both Windows registry views and checks the corresponding desktop library. When missing, installation is mandatory. When present, optional repair downloads the exact installed patch version. The application targets runtime 10.0.0 with normal patch roll-forward, so an existing stable 10.0 runtime is sufficient.

Runtime downloads use Microsoft's release metadata, SHA-512 hashes, and Authenticode signatures. Downloads go into Setup's temporary directory. Setup requests administrator approval only for a system-wide installation and when installing or repairing the runtime; a per-user installation with the runtime already present never prompts. The verified Microsoft EXE runs with `/install` or `/repair`, `/quiet`, and `/norestart`. Cancellation or download failure stops setup before application installation. A required reboot is reported instead of forced. Setup removes its temporary files; uninstall leaves the shared runtime installed.

Installed applications use the Setup asset during updates, preserving the installation directory, install mode, and recorded shortcut choices.

The package is not code-signed. Production signing requires a publisher certificate.
