using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkdownPro.Models
{
    public partial class MarkdownDocument : ObservableObject
    {
        public const string DefaultStarterTemplate = @"# Welcome to Markdown Pro

Built-in support for **Markdown** and **Mermaid** diagrams.

## 1. Example Mermaid Diagram
```mermaid
graph LR
    rawTextNode[Raw Text] --> markedNode(Marked.js)
    markedNode --> viewDecisionNode{Rendered View}
    viewDecisionNode -->|Success| successNode([Success])

    style rawTextNode fill:#e1f5fe,stroke:#03a9f4,stroke-width:2px
    style markedNode fill:#fff8e1,stroke:#ffb300,stroke-width:2px
    style viewDecisionNode fill:#ede7f6,stroke:#5e35b1,stroke-width:2px
    style successNode fill:#e8f5e9,stroke:#2e7d32,stroke-width:2px
```

## 2. Dynamic Tables
| Feature | Supported |
| :--- | :--- |
| Real-time | Yes |
| Export | Yes |
| Privacy | 100% |

> Click **Export** to download this file as a PDF or Markdown document..";

        public Guid Id { get; } = Guid.NewGuid();

        private string _title = "Untitled.md";
        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged(nameof(DisplayTitle));
                    OnPropertyChanged(nameof(ToolTipPath));
                }
            }
        }

        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    OnPropertyChanged(nameof(ToolTipPath));
                }
            }
        }

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set
            {
                if (SetProperty(ref _content, value))
                {
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        private string _savedContent = string.Empty;
        public string SavedContent
        {
            get => _savedContent;
            set
            {
                if (SetProperty(ref _savedContent, value))
                {
                    OnPropertyChanged(nameof(IsDirty));
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public bool IsDirty => !string.Equals(Content, SavedContent, StringComparison.Ordinal);

        public string DisplayTitle => IsDirty ? $"{Title} *" : Title;

        public string ToolTipPath => string.IsNullOrWhiteSpace(FilePath) ? $"{Title} (Unsaved)" : FilePath;

        public static MarkdownDocument CreateStarterDocument()
        {
            return new MarkdownDocument
            {
                Title = "Welcome.md",
                FilePath = null,
                Content = DefaultStarterTemplate,
                SavedContent = DefaultStarterTemplate
            };
        }

        public static MarkdownDocument CreateUntitled(int index)
        {
            string name = index <= 1 ? "Untitled.md" : $"Untitled-{index}.md";
            return new MarkdownDocument
            {
                Title = name,
                FilePath = null,
                Content = string.Empty,
                SavedContent = string.Empty
            };
        }

        public static MarkdownDocument LoadFromFile(string fullPath)
        {
            string text = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
            return new MarkdownDocument
            {
                Title = Path.GetFileName(fullPath),
                FilePath = fullPath,
                Content = text,
                SavedContent = text
            };
        }

        public void MarkSaved(string? newPath = null)
        {
            if (!string.IsNullOrWhiteSpace(newPath))
            {
                FilePath = newPath;
                Title = Path.GetFileName(newPath);
            }
            SavedContent = Content;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }
}

