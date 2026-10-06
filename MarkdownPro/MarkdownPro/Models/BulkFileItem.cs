using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkdownPro.Models
{
    public partial class BulkFileItem : ObservableObject
    {
        private int _index;
        public int Index
        {
            get => _index;
            set
            {
                if (SetProperty(ref _index, value))
                {
                    OnPropertyChanged(nameof(IndexBadge));
                }
            }
        }

        private string _fileName = string.Empty;
        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        private string _fullPath = string.Empty;
        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        private long _sizeBytes;
        public long SizeBytes
        {
            get => _sizeBytes;
            set
            {
                if (SetProperty(ref _sizeBytes, value))
                {
                    OnPropertyChanged(nameof(SizeText));
                }
            }
        }

        private bool _canMoveUp;
        public bool CanMoveUp
        {
            get => _canMoveUp;
            set => SetProperty(ref _canMoveUp, value);
        }

        private bool _canMoveDown;
        public bool CanMoveDown
        {
            get => _canMoveDown;
            set => SetProperty(ref _canMoveDown, value);
        }

        public string IndexBadge => Index.ToString();

        public string SizeText => $"{(SizeBytes / 1024.0):F1} KB";
    }
}

