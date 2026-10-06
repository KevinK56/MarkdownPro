using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkdownPro.Models
{
    public partial class FolderNode : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";
        public ObservableCollection<FolderNode> Children { get; } = new();
    }
}

