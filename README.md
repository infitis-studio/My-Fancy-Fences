<div align="center">
  <img src="assets/my-fancy-fences.svg" width="88" alt="My Fancy Fences logo" />

  <h1>My Fancy Fences</h1>

  <p>
    <img src="https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square&logo=windows11&logoColor=white" alt="Windows" />
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10" />
    <img src="https://img.shields.io/badge/license-MIT-2ea44f?style=flat-square" alt="MIT License" />
    <img src="https://img.shields.io/github/issues/infitis-studio/my-fancy-fences?style=flat-square&color=e3b341" alt="Open issues" />
  </p>

  <p>
    <em>Desktop panels, shortcuts, layouts, and wallpapers in one clean Windows tool.</em><br />
    <strong>Organize faster. Switch layouts instantly. Bring your desktop to life.</strong>
  </p>
</div>

---

## Screenshots

### 🖥️ Desktop panels

![My Fancy Fences desktop panels](assets/screenshots/desktop-panels.png)

### 🌄 Wallpaper browser

![My Fancy Fences wallpaper browser](assets/screenshots/wallpaper-browser.png)

## ✨ Features

- 🧩 Create clean desktop panels for apps, files, folders, and shortcuts
- 🖱️ Drag and drop items directly onto panels
- 🗂️ Save multiple desktop layouts and switch between them instantly
- ⌨️ Assign global hotkeys for layout switching
- 🎨 Customize colors, transparency, borders, typography, and icon size
- 🌄 Browse, favorite, download, and set wallpapers inside the app
- 🎞️ Set static wallpapers and experimental live wallpapers
- ⚙️ Manage everything from one polished management window
- 📦 Use tray controls, optional startup, import/export, and automatic backups

## 🖼️ Wallpaper Sources

My Fancy Fences can browse wallpapers from third-party sources:

- 🔵 [Wallhaven](https://wallhaven.cc/) — static wallpapers through the Wallhaven API.
- 🟢 [MotionBGs / MoeWalls](https://motionbgs.com/) — animated live wallpapers and previews.

Wallpapers remain the property of their original owners. My Fancy Fences does not include wallpaper files in the repository or release packages; it only helps users browse, preview, download, and apply wallpapers from supported sources.

## 🚀 Quick Start

### Requirements

- Windows 10 or Windows 11
- .NET 10 Desktop Runtime for the smaller `REQUIRES NET10` build
- No additional runtime for the `WITH NET10` build

### Build and run

```powershell
dotnet build "My Fancy Fences.slnx"
dotnet run --project "My Fancy Fences/My Fancy Fences.csproj"
```

## 🧭 Usage

- Double-click a panel header to edit that panel.
- Use the Creator panel to add panels or open the management window.
- Use the management window to edit panels, browse wallpapers, change shared appearance, and import/export settings.
- Click the empty-panel prompt or drag files/apps/shortcuts onto a panel to create shortcuts.
- Choose single-click or double-click activation for panel items.
- Double-click the tray icon to open the management window.
- Right-click the tray icon to manage startup, open tools, or close the application.

## 🛠️ Built With

- WPF and .NET 10
- [MahApps.Metro.IconPacks.Lucide](https://github.com/MahApps/MahApps.Metro.IconPacks)
- [Wallhaven API](https://wallhaven.cc/help/api)
- [MotionBGs / MoeWalls](https://motionbgs.com/)

## 🧪 Project Status

My Fancy Fences is under active development and may still contain bugs. Features and saved-settings formats may change between versions.

## 🤝 Contributing

Issues, suggestions, and pull requests are welcome.

## 📄 License

My Fancy Fences is available under the [MIT License](LICENSE).
