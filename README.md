# Markdown Pro — Editor & Mermaid Previewer (WinUI 3)

**Markdown Pro** is a fast, native **WinUI 3** dual-pane Markdown and **Mermaid.js v11.4.0** authoring environment for Windows 10 and Windows 11. It runs **100% offline** with local rendering, multi-tab document editing, folder explorer navigation, bulk `.md` file merging with automatic PDF page breaks, and one-click PDF/HTML export.


---
## Preview

<img width="1914" height="1029" alt="image" src="https://github.com/user-attachments/assets/b0f422e2-14a9-48fd-af29-ee9e692e4a65" />

<img width="1915" height="1024" alt="image" src="https://github.com/user-attachments/assets/cbb22d61-f5da-4826-87e4-f222e334839d" />

<img width="1909" height="1023" alt="image" src="https://github.com/user-attachments/assets/a3d857c5-d720-4ee7-adc5-ecc01115eb7f" />

---

## ✨ Key Features

- **Native Windows Menu Bar & Multi-View Workspace**:
  - Clean `File | Tools | Preferences` menu bar with full keyboard shortcut support (`Ctrl+N`, `Ctrl+O`, `Ctrl+Shift+O`, `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+P`, `Ctrl+B`, `Ctrl+I`).
  - Instant view switching between **Editor**, **Split Preview (50/50)**, and **Full Preview**.
- **100% Offline Markdown & Mermaid v11.4.0 Engine**:
  - Powered by locally bundled `Marked.js v15.0.12` and `Mermaid.js v11.4.0` inside WebView2.
  - Isolated per-diagram syntax error boundaries so a typo in one Mermaid diagram never breaks the rest of your document.
- **Multi-Tab Dark Editor & Explorer Sidebar**:
  - VS Code-inspired dark code editor (`#1e1e1e`) with synchronized line numbers, dirty indicator (`*`), and tab management.
  - Collapsible Explorer Sidebar showing **Open Documents** and **Open Folder** file trees with parent/subfolder navigation.
- **Bulk Markdown Import & Merge (`Up to 20 .md Files`)**:
  - Combine up to 20 Markdown files in custom order with automatic `<div style="page-break-after: always;"></div>` dividers for multi-chapter PDF generation.
- **Interactive Syntax & Mermaid v11.4.0 Reference Guides**:
  - Built-in cheat sheets for Markdown syntax, node shapes, custom CSS classes, and one-click copyable Flowchart, Sequence, and ER diagram templates.
- **Print-Optimized PDF & HTML Export**:
  - Export directly to `.md`, standalone GitHub-styled `.html`, or print-optimized `.pdf` with light/dark preview themes.
- **GitHub Auto-Update Service**:
  - Automatically checks [GitHub Releases](https://github.com/KevinK56/MarkdownPro/releases) for newer versions and downloads/installs updates in one click.
- **Example File**:
  - See example file in the main directory or click here: [Example.md](https://github.com/KevinK56/MarkdownPro/blob/master/Example.md)
---

## 📦 Installation

### Option 1: Windows Installer (`MarkdownPro-Setup-vX.Y.Z.exe`) — *Recommended*
1. Download the latest `MarkdownPro-Setup-v*.exe` from [**GitHub Releases**](https://github.com/KevinK56/MarkdownPro/releases/latest).
2. Run the installer, review and accept the **License & Terms of Use**, and optionally enable:
   - Desktop shortcut
   - File associations for `.md`, `.markdown`, `.mdown`, and `.mkd` files

### Option 2: Portable Archive (`MarkdownPro-Portable-vX.Y.Z-win-x64.zip`)
1. Download `MarkdownPro-Portable-v*-win-x64.zip` from [**GitHub Releases**](https://github.com/KevinK56/MarkdownPro/releases/latest).
2. Extract the archive to any folder and launch `MarkdownPro.exe`.

---

## 🛠️ Building from Source & Creating the Installer

### Prerequisites
- **.NET 8.0 SDK** (with Windows App SDK / WinUI 3 workloads)
- **Windows 10 SDK** (`10.0.19041.0` or newer)
- [**Inno Setup 6**](https://jrsoftware.org/isdl.php) (to compile the `.exe` setup wizard)

### Build Scripts
- **Build Portable Release**:
  ```bat
  build-portable.bat
  ```
  Publishes a self-contained `win-x64` build to `publish\MarkdownPro-Portable-win-x64\`.

- **Build Windows Setup Installer**:
  ```bat
  build-installer.bat
  ```
  Publishes the self-contained WinUI 3 application and compiles `installer.iss` using Inno Setup 6 into `installer_output\MarkdownPro-Setup-v1.0.0.exe`.

---

## 📄 License

Licensed under the [GNU General Public License v3.0 (GPL-3.0)](LICENSE).
# MarkdownPro
