using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MarkdownPro.Models;
using MarkdownPro.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.System;

namespace MarkdownPro
{
    public sealed partial class MainWindow : Window
    {
        private readonly ObservableCollection<MarkdownDocument> _openDocuments = new();
        private readonly ObservableCollection<FolderNode> _folderNodes = new();
        private readonly ObservableCollection<BulkFileItem> _bulkFiles = new();

        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _renderDebounceTimer;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _infoBarTimer;

        private SessionState _session = new();
        private MarkdownDocument? _activeDocument;
        private bool _isSuppressingEditorTextChange;
        private bool _isSuppressingTabSelectionSync;
        private bool _isWebViewReady;
        private string _lastRenderedInnerHtml = string.Empty;
        private string _currentPreviewTheme = "light";
        private string _currentPreviewLayout = "editor"; // "editor", "split", "full"
        private string _previousPreviewLayoutBeforeFull = "split";
        private string _currentSection = "editor"; // "editor", "bulk", "help", "mermaid"
        private string? _currentFolderPath;
        private string? _rootOpenedFolderPath;
        private int _untitledCounter = 1;
        private bool _hasShownDefaultAppPrompt;
        private UpdateInfo? _availableUpdate;
        private bool _isDownloadingUpdate;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(IReadOnlyList<string>? initialFiles)
        {
            InitializeComponent();

            OpenDocumentsListView.ItemsSource = _openDocuments;
            FolderFilesListView.ItemsSource = _folderNodes;
            BulkFileListView.ItemsSource = _bulkFiles;

            _renderDebounceTimer = DispatcherQueue.CreateTimer();
            _renderDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
            _renderDebounceTimer.IsRepeating = false;
            _renderDebounceTimer.Tick += async (_, _) => await RenderMarkdownNowAsync();

            _infoBarTimer = DispatcherQueue.CreateTimer();
            _infoBarTimer.Interval = TimeSpan.FromSeconds(3.5);
            _infoBarTimer.IsRepeating = false;
            _infoBarTimer.Tick += (_, _) => AppInfoBar.IsOpen = false;

            Closed += MainWindow_Closed;
            RootGrid.Loaded += RootGrid_Loaded;

            InitializeWorkspaceState(initialFiles);
        }

        private void InitializeWorkspaceState(IReadOnlyList<string>? initialFiles)
        {
            _session = SessionService.Load();

            // Always start in Editor-Only view as specified, while preserving sidebar and theme preferences
            if (!_session.IsSidebarOpen)
            {
                SidebarColumn.Width = new GridLength(0);
                SidebarBorder.Visibility = Visibility.Collapsed;
                MenuShowSidebar.IsChecked = false;
            }
            else
            {
                MenuShowSidebar.IsChecked = true;
            }

            MenuCheckUpdatesStartup.IsChecked = _session.CheckForUpdatesOnStartup;

            _currentPreviewTheme = string.Equals(_session.PreviewTheme, "dark", StringComparison.OrdinalIgnoreCase)
                ? "dark"
                : "light";
            MenuThemeLight.IsChecked = _currentPreviewTheme != "dark";
            MenuThemeDark.IsChecked = _currentPreviewTheme == "dark";

            if (!string.IsNullOrWhiteSpace(_session.LastFolderPath) && Directory.Exists(_session.LastFolderPath))
            {
                LoadFolderIntoSidebar(_session.LastFolderPath, setAsRoot: true);
            }

            // Restore previously open files from session
            if (_session.OpenFilePaths != null)
            {
                foreach (string path in _session.OpenFilePaths)
                {
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        OpenOrActivateFile(path, switchToTab: false);
                    }
                }
            }

            // Open any files passed via command line or file activation
            if (initialFiles != null && initialFiles.Count > 0)
            {
                foreach (string file in initialFiles)
                {
                    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                    {
                        OpenOrActivateFile(file, switchToTab: true);
                    }
                }
            }

            // If no files were opened, create the default Welcome to Markdown Pro starter document
            if (_openDocuments.Count == 0)
            {
                var starter = MarkdownDocument.CreateStarterDocument();
                AddDocumentTab(starter, selectTab: true);
            }
            else if (_activeDocument == null)
            {
                var targetDoc = _openDocuments.FirstOrDefault(d =>
                    !string.IsNullOrWhiteSpace(_session.ActiveFilePath) &&
                    string.Equals(d.FilePath, _session.ActiveFilePath, StringComparison.OrdinalIgnoreCase))
                    ?? _openDocuments[0];

                ActivateDocument(targetDoc);
            }

            SetActiveSection("editor");
            SetPreviewLayout("editor");
        }

        private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeWebViewAsync();

            if (!_session.HasAcceptedLicense)
            {
                await PromptLicenseAcceptanceOnFirstRunAsync();
            }

            if (!_hasShownDefaultAppPrompt)
            {
                _hasShownDefaultAppPrompt = true;
                await PromptDefaultMarkdownAppIfNeededAsync();
            }

            if (_session.CheckForUpdatesOnStartup)
            {
                _ = CheckForUpdatesInBackgroundAsync();
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                await PreviewWebView.EnsureCoreWebView2Async();
                var core = PreviewWebView.CoreWebView2;

                string webAssetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Web");
                if (Directory.Exists(webAssetsFolder))
                {
                    core.SetVirtualHostNameToFolderMapping(
                        "appassets.local",
                        webAssetsFolder,
                        CoreWebView2HostResourceAccessKind.Allow);
                }

                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;

                core.WebMessageReceived += CoreWebView2_WebMessageReceived;
                core.Navigate("https://appassets.local/preview.html");
            }
            catch (Exception ex)
            {
                TriggerNotification($"WebView2 initialization failed: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp))
                {
                    return;
                }

                string? type = typeProp.GetString();
                if (type == "ready")
                {
                    _isWebViewReady = true;
                    ApplyPreviewThemeToWebView(_currentPreviewTheme);
                    ApplyFocusModeToWebView(_currentPreviewLayout == "full");
                    await RenderMarkdownNowAsync();
                }
                else if (type == "rendered")
                {
                    if (root.TryGetProperty("timestamp", out var tsProp))
                    {
                        RenderTimeText.Text = $"Rendered: {tsProp.GetString()}";
                    }
                    if (root.TryGetProperty("innerHtml", out var htmlProp))
                    {
                        _lastRenderedInnerHtml = htmlProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch
            {
                // Ignore malformed messages
            }
        }

        #region Default .md Application Prompt & Registration

        private async Task PromptDefaultMarkdownAppIfNeededAsync()
        {
            if (_session.SuppressDefaultAppPrompt || FileAssociationService.IsRegisteredAsHandler())
            {
                return;
            }

            if (RootGrid.XamlRoot == null)
            {
                return;
            }

            var dontAskCheckBox = new CheckBox
            {
                Content = "Don't ask me again on startup",
                Margin = new Thickness(0, 12, 0, 0)
            };

            var contentStack = new StackPanel { Spacing = 8 };
            contentStack.Children.Add(new TextBlock
            {
                Text = "Would you like to set Markdown Pro as your default application for opening Markdown (.md, .markdown) files?",
                TextWrapping = TextWrapping.Wrap
            });
            contentStack.Children.Add(dontAskCheckBox);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Set Markdown Pro as Default .md App?",
                Content = contentStack,
                PrimaryButtonText = "Set as Default",
                CloseButtonText = "Not Now",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (dontAskCheckBox.IsChecked == true)
            {
                _session.SuppressDefaultAppPrompt = true;
                SaveSession();
            }

            if (result == ContentDialogResult.Primary)
            {
                bool ok = await FileAssociationService.RegisterFileAssociationsAsync(openWindowsDefaultAppsSettings: true);
                if (ok)
                {
                    TriggerNotification("Registered Markdown Pro for .md files! Select Markdown Pro in Windows Default Apps if prompted.", InfoBarSeverity.Success);
                }
            }
        }

        private async void SetDefaultMdApp_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await FileAssociationService.RegisterFileAssociationsAsync(openWindowsDefaultAppsSettings: true);
            if (ok)
            {
                TriggerNotification("Registered .md file associations and opened Windows Default Apps settings.", InfoBarSeverity.Success);
            }
            else
            {
                TriggerNotification("Could not update registry associations.", InfoBarSeverity.Warning);
            }
        }

        public void OpenFilesFromExternalActivation(IEnumerable<string> filePaths)
        {
            foreach (string path in filePaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    OpenOrActivateFile(path, switchToTab: true);
                }
            }
            SetActiveSection("editor");
        }

        #endregion

        #region Multi-Tab Document Management

        private void AddDocumentTab(MarkdownDocument doc, bool selectTab)
        {
            _openDocuments.Add(doc);

            var tabItem = new TabViewItem
            {
                Header = doc.DisplayTitle,
                Tag = doc,
                IconSource = new FontIconSource { Glyph = "\uE8A5" }
            };
            ToolTipService.SetToolTip(tabItem, doc.ToolTipPath);

            PropertyChangedEventHandler handler = (_, args) =>
            {
                if (args.PropertyName == nameof(MarkdownDocument.DisplayTitle) ||
                    args.PropertyName == nameof(MarkdownDocument.ToolTipPath))
                {
                    tabItem.Header = doc.DisplayTitle;
                    ToolTipService.SetToolTip(tabItem, doc.ToolTipPath);
                    if (_activeDocument == doc)
                    {
                        UpdateActiveDocumentHeaderAndStatus();
                    }
                }
            };
            doc.PropertyChanged += handler;

            DocumentTabView.TabItems.Add(tabItem);

            if (selectTab)
            {
                ActivateDocument(doc);
            }
        }

        private void ActivateDocument(MarkdownDocument doc)
        {
            _activeDocument = doc;
            _isSuppressingTabSelectionSync = true;

            try
            {
                var matchingTab = DocumentTabView.TabItems
                    .OfType<TabViewItem>()
                    .FirstOrDefault(t => t.Tag == doc);

                if (matchingTab != null && !ReferenceEquals(DocumentTabView.SelectedItem, matchingTab))
                {
                    DocumentTabView.SelectedItem = matchingTab;
                }

                if (OpenDocumentsListView.SelectedItem != doc)
                {
                    OpenDocumentsListView.SelectedItem = doc;
                }

                _isSuppressingEditorTextChange = true;
                EditorTextBox.Text = doc.Content;
                _isSuppressingEditorTextChange = false;

                UpdateLineNumbersAndStats();
                UpdateActiveDocumentHeaderAndStatus();
                QueueDebouncedRender();
            }
            finally
            {
                _isSuppressingTabSelectionSync = false;
            }
        }

        public void OpenOrActivateFile(string fullPath, bool switchToTab = true)
        {
            string normalizedPath = Path.GetFullPath(fullPath);
            var existing = _openDocuments.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath) &&
                string.Equals(Path.GetFullPath(d.FilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                if (switchToTab)
                {
                    ActivateDocument(existing);
                }
                return;
            }

            var loadedDoc = MarkdownDocument.LoadFromFile(normalizedPath);
            AddDocumentTab(loadedDoc, selectTab: switchToTab);
            SaveSession();
        }

        private async Task<bool> CloseDocumentAsync(MarkdownDocument doc)
        {
            if (doc.IsDirty && RootGrid.XamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "Save changes?",
                    Content = $"Do you want to save changes to '{doc.Title}' before closing?",
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Don't Save",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return false;
                }

                if (result == ContentDialogResult.Primary)
                {
                    bool saved = await SaveDocumentInternalAsync(doc, forceSaveAs: false);
                    if (!saved)
                    {
                        return false;
                    }
                }
            }

            var matchingTab = DocumentTabView.TabItems
                .OfType<TabViewItem>()
                .FirstOrDefault(t => t.Tag == doc);

            if (matchingTab != null)
            {
                DocumentTabView.TabItems.Remove(matchingTab);
            }

            _openDocuments.Remove(doc);

            if (_openDocuments.Count == 0)
            {
                _untitledCounter++;
                var untitled = MarkdownDocument.CreateUntitled(_untitledCounter);
                AddDocumentTab(untitled, selectTab: true);
            }
            else if (_activeDocument == doc)
            {
                ActivateDocument(_openDocuments[^1]);
            }

            SaveSession();
            return true;
        }

        private void DocumentTabView_AddTabButtonClick(TabView sender, object args)
        {
            CreateNewUntitledDocument();
        }

        private async void DocumentTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Tab?.Tag is MarkdownDocument doc)
            {
                await CloseDocumentAsync(doc);
            }
        }

        private void DocumentTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSuppressingTabSelectionSync)
            {
                return;
            }

            if (DocumentTabView.SelectedItem is TabViewItem tabItem && tabItem.Tag is MarkdownDocument doc)
            {
                ActivateDocument(doc);
            }
        }

        private void OpenDocumentsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSuppressingTabSelectionSync)
            {
                return;
            }

            if (OpenDocumentsListView.SelectedItem is MarkdownDocument doc)
            {
                ActivateDocument(doc);
                SetActiveSection("editor");
            }
        }

        private async void CloseDocumentFromSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MarkdownDocument doc)
            {
                await CloseDocumentAsync(doc);
            }
        }

        private void CreateNewUntitledDocument()
        {
            _untitledCounter++;
            var doc = MarkdownDocument.CreateUntitled(_untitledCounter);
            AddDocumentTab(doc, selectTab: true);
            SetActiveSection("editor");
            EditorTextBox.Focus(FocusState.Programmatic);
        }

        #endregion

        #region File Operations (New, Open File, Open Folder, Save, Save As)

        private void NewDocument_Click(object sender, RoutedEventArgs e) => CreateNewUntitledDocument();

        private async void OpenFile_Click(object sender, RoutedEventArgs e) => await PickAndOpenFileAsync();

        private async void OpenFolder_Click(object sender, RoutedEventArgs e) => await PickAndOpenFolderAsync();

        private async void SaveDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDocument != null)
            {
                await SaveDocumentInternalAsync(_activeDocument, forceSaveAs: false);
            }
        }

        private async void SaveAsDocument_Click(object sender, RoutedEventArgs e)
        {
            if (_activeDocument != null)
            {
                await SaveDocumentInternalAsync(_activeDocument, forceSaveAs: true);
            }
        }

        private async Task PickAndOpenFileAsync()
        {
            var picker = new FileOpenPicker();
            InitializePickerWithWindow(picker);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");
            picker.FileTypeFilter.Add(".mdown");
            picker.FileTypeFilter.Add(".txt");

            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0)
            {
                return;
            }

            foreach (var file in files)
            {
                OpenOrActivateFile(file.Path, switchToTab: true);
            }

            SetActiveSection("editor");
        }

        private async Task PickAndOpenFolderAsync()
        {
            var picker = new FolderPicker();
            InitializePickerWithWindow(picker);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            if (SidebarBorder.Visibility == Visibility.Collapsed)
            {
                SidebarColumn.Width = new GridLength(250);
                SidebarBorder.Visibility = Visibility.Visible;
                _session.IsSidebarOpen = true;
                MenuShowSidebar.IsChecked = true;
            }

            LoadFolderIntoSidebar(folder.Path, setAsRoot: true);
            SaveSession();
            TriggerNotification($"Opened folder: {folder.Name}", InfoBarSeverity.Informational);
        }

        private void LoadFolderIntoSidebar(string folderPath, bool setAsRoot)
        {
            if (!Directory.Exists(folderPath))
            {
                return;
            }

            _currentFolderPath = Path.GetFullPath(folderPath);
            if (setAsRoot || string.IsNullOrWhiteSpace(_rootOpenedFolderPath))
            {
                _rootOpenedFolderPath = _currentFolderPath;
            }

            CurrentFolderNameText.Text = Path.GetFileName(_currentFolderPath.TrimEnd(Path.DirectorySeparatorChar));
            ToolTipService.SetToolTip(CurrentFolderNameText, _currentFolderPath);

            _folderNodes.Clear();

            try
            {
                // Add parent navigation row if inside a subfolder of the root opened folder
                if (!string.IsNullOrWhiteSpace(_rootOpenedFolderPath) &&
                    !string.Equals(_currentFolderPath, _rootOpenedFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    var parentDir = Directory.GetParent(_currentFolderPath);
                    if (parentDir != null)
                    {
                        _folderNodes.Add(new FolderNode
                        {
                            Name = ".. (Up a folder)",
                            FullPath = parentDir.FullName,
                            IsDirectory = true
                        });
                    }
                }

                foreach (var dir in Directory.GetDirectories(_currentFolderPath).OrderBy(Path.GetFileName))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith('.'))
                    {
                        continue;
                    }

                    _folderNodes.Add(new FolderNode
                    {
                        Name = dirName,
                        FullPath = dir,
                        IsDirectory = true
                    });
                }

                string[] allowedExtensions = { ".md", ".markdown", ".mdown", ".mkd" };
                foreach (var file in Directory.GetFiles(_currentFolderPath).OrderBy(Path.GetFileName))
                {
                    string ext = Path.GetExtension(file);
                    if (allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                    {
                        _folderNodes.Add(new FolderNode
                        {
                            Name = Path.GetFileName(file),
                            FullPath = file,
                            IsDirectory = false
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                TriggerNotification($"Error reading folder: {ex.Message}", InfoBarSeverity.Warning);
            }

            EmptyFolderPlaceholder.Visibility = Visibility.Collapsed;
            FolderFilesListView.Visibility = Visibility.Visible;
        }

        private void FolderFilesListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not FolderNode node)
            {
                return;
            }

            if (node.IsDirectory)
            {
                LoadFolderIntoSidebar(node.FullPath, setAsRoot: false);
            }
            else
            {
                OpenOrActivateFile(node.FullPath, switchToTab: true);
                SetActiveSection("editor");
            }
        }

        private async void NewFileInFolder_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentFolderPath) || !Directory.Exists(_currentFolderPath))
            {
                await PickAndOpenFolderAsync();
                return;
            }

            if (RootGrid.XamlRoot == null)
            {
                return;
            }

            var nameBox = new TextBox
            {
                PlaceholderText = "document.md",
                Text = "new-note.md"
            };

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "New Markdown File in Folder",
                Content = nameBox,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(nameBox.Text))
            {
                return;
            }

            string fileName = nameBox.Text.Trim();
            if (!fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".md";
            }

            string fullPath = Path.Combine(_currentFolderPath, fileName);
            try
            {
                if (!File.Exists(fullPath))
                {
                    await File.WriteAllTextAsync(fullPath, $"# {Path.GetFileNameWithoutExtension(fileName)}{Environment.NewLine}{Environment.NewLine}", Encoding.UTF8);
                }

                LoadFolderIntoSidebar(_currentFolderPath, setAsRoot: false);
                OpenOrActivateFile(fullPath, switchToTab: true);
                SetActiveSection("editor");
                TriggerNotification($"Created {fileName}", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                TriggerNotification($"Could not create file: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private void RefreshFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
            {
                LoadFolderIntoSidebar(_currentFolderPath, setAsRoot: false);
                TriggerNotification("Folder refreshed.", InfoBarSeverity.Informational);
            }
        }

        private async Task<bool> SaveDocumentInternalAsync(MarkdownDocument doc, bool forceSaveAs)
        {
            string? targetPath = doc.FilePath;
            if (forceSaveAs || string.IsNullOrWhiteSpace(targetPath))
            {
                var picker = new FileSavePicker();
                InitializePickerWithWindow(picker);
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeChoices.Add("Markdown Document", new List<string> { ".md", ".markdown" });
                picker.SuggestedFileName = string.IsNullOrWhiteSpace(doc.Title)
                    ? $"markdownpro-doc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.md"
                    : doc.Title;

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    return false;
                }
                targetPath = file.Path;
            }

            try
            {
                await File.WriteAllTextAsync(targetPath, doc.Content, Encoding.UTF8);
                doc.MarkSaved(targetPath);
                UpdateActiveDocumentHeaderAndStatus();
                if (!string.IsNullOrWhiteSpace(_currentFolderPath) && Directory.Exists(_currentFolderPath))
                {
                    LoadFolderIntoSidebar(_currentFolderPath, setAsRoot: false);
                }
                SaveSession();
                TriggerNotification($"Saved {doc.Title}", InfoBarSeverity.Success);
                return true;
            }
            catch (Exception ex)
            {
                TriggerNotification($"Failed to save file: {ex.Message}", InfoBarSeverity.Error);
                return false;
            }
        }

        #endregion

        #region Sidebar, Section Navigation & Preview View Modes

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            bool isCurrentlyOpen = SidebarBorder.Visibility == Visibility.Visible;
            if (isCurrentlyOpen)
            {
                SidebarColumn.Width = new GridLength(0);
                SidebarBorder.Visibility = Visibility.Collapsed;
                _session.IsSidebarOpen = false;
                MenuShowSidebar.IsChecked = false;
            }
            else
            {
                SidebarColumn.Width = new GridLength(250);
                SidebarBorder.Visibility = Visibility.Visible;
                _session.IsSidebarOpen = true;
                MenuShowSidebar.IsChecked = true;
            }
            SaveSession();
        }

        private void NavEditor_Click(object sender, RoutedEventArgs e) => SetActiveSection("editor");
        private void NavBulk_Click(object sender, RoutedEventArgs e) => SetActiveSection("bulk");
        private void NavHelp_Click(object sender, RoutedEventArgs e) => SetActiveSection("help");
        private void NavMermaid_Click(object sender, RoutedEventArgs e) => SetActiveSection("mermaid");

        private void SetActiveSection(string section)
        {
            _currentSection = section;

            EditorSectionGrid.Visibility = section == "editor" ? Visibility.Visible : Visibility.Collapsed;
            BulkSectionGrid.Visibility = section == "bulk" ? Visibility.Visible : Visibility.Collapsed;
            HelpSectionGrid.Visibility = section == "help" ? Visibility.Visible : Visibility.Collapsed;
            MermaidSectionGrid.Visibility = section == "mermaid" ? Visibility.Visible : Visibility.Collapsed;

            // If user clicks a left-pane section while in Full Preview mode, switch to Split mode so the section is visible
            if (_currentPreviewLayout == "full")
            {
                SetPreviewLayout("split");
            }
        }

        private static void HighlightNavButton(Button btn, bool isActive)
        {
            if (isActive)
            {
                btn.Background = new SolidColorBrush(ColorHelper.FromArgb(255, 9, 105, 218));
                btn.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                btn.Background = new SolidColorBrush(Colors.Transparent);
                btn.ClearValue(Control.ForegroundProperty);
            }
        }

        private void ModeEditorOnly_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSection("editor");
            SetPreviewLayout("editor");
        }

        private void ModeSplitPreview_Click(object sender, RoutedEventArgs e) => SetPreviewLayout("split");
        private void ModeFullPreview_Click(object sender, RoutedEventArgs e) => SetPreviewLayout("full");

        private void ToggleExpandPreview_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreviewLayout == "full")
            {
                SetPreviewLayout(_previousPreviewLayoutBeforeFull == "full" ? "split" : _previousPreviewLayoutBeforeFull);
            }
            else
            {
                _previousPreviewLayoutBeforeFull = _currentPreviewLayout;
                SetPreviewLayout("full");
            }
        }

        private void SetPreviewLayout(string layout)
        {
            if (layout != "full")
            {
                _previousPreviewLayoutBeforeFull = layout;
            }

            _currentPreviewLayout = layout;
            _session.PreviewLayout = layout;

            if (layout == "editor")
            {
                AuthoringWorkspaceGrid.Visibility = Visibility.Visible;
                PreviewContainerGrid.Visibility = Visibility.Collapsed;
                AuthoringColumn.Width = new GridLength(1, GridUnitType.Star);
                PreviewColumn.Width = new GridLength(0);

                ExpandPreviewIcon.Glyph = "\uE740";
                ToolTipService.SetToolTip(ExpandPreviewBtn, "Expand Focus View");
                ApplyFocusModeToWebView(false);
            }
            else if (layout == "split")
            {
                AuthoringWorkspaceGrid.Visibility = Visibility.Visible;
                PreviewContainerGrid.Visibility = Visibility.Visible;
                PreviewContainerGrid.Padding = new Thickness(0, 12, 12, 12);
                AuthoringColumn.Width = new GridLength(1, GridUnitType.Star);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

                ExpandPreviewIcon.Glyph = "\uE740";
                ToolTipService.SetToolTip(ExpandPreviewBtn, "Expand Focus View");
                ApplyFocusModeToWebView(false);
                _ = RenderMarkdownNowAsync();
            }
            else if (layout == "full")
            {
                AuthoringWorkspaceGrid.Visibility = Visibility.Collapsed;
                PreviewContainerGrid.Visibility = Visibility.Visible;
                PreviewContainerGrid.Padding = new Thickness(12);
                AuthoringColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);

                ExpandPreviewIcon.Glyph = "\uE73F";
                ToolTipService.SetToolTip(ExpandPreviewBtn, "Contract View");
                ApplyFocusModeToWebView(true);
                _ = RenderMarkdownNowAsync();
            }

            HighlightNavButton(ModeEditorOnlyBtn, layout == "editor");
            HighlightNavButton(ModeSplitPreviewBtn, layout == "split");
            HighlightNavButton(ModeFullPreviewBtn, layout == "full");
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e)
        {
            _currentPreviewTheme = "light";
            _session.PreviewTheme = "light";
            MenuThemeLight.IsChecked = true;
            MenuThemeDark.IsChecked = false;
            ApplyPreviewThemeToWebView("light");
            SaveSession();
        }

        private void MenuThemeDark_Click(object sender, RoutedEventArgs e)
        {
            _currentPreviewTheme = "dark";
            _session.PreviewTheme = "dark";
            MenuThemeLight.IsChecked = false;
            MenuThemeDark.IsChecked = true;
            ApplyPreviewThemeToWebView("dark");
            SaveSession();
        }

        private void ApplyPreviewThemeToWebView(string theme)
        {
            bool isDark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase);
            if (PreviewCardBorder != null && PreviewHeaderBorder != null && LivePreviewHeaderTitle != null)
            {
                PreviewCardBorder.Background = new SolidColorBrush(
                    isDark ? ColorHelper.FromArgb(255, 13, 17, 23) : Colors.White);
                PreviewHeaderBorder.Background = new SolidColorBrush(
                    isDark ? ColorHelper.FromArgb(255, 22, 27, 34) : ColorHelper.FromArgb(255, 248, 249, 250));
                PreviewHeaderBorder.BorderBrush = new SolidColorBrush(
                    isDark ? ColorHelper.FromArgb(255, 48, 54, 61) : ColorHelper.FromArgb(255, 233, 236, 239));
                LivePreviewHeaderTitle.Foreground = new SolidColorBrush(
                    isDark ? ColorHelper.FromArgb(255, 201, 209, 217) : ColorHelper.FromArgb(255, 36, 41, 46));
            }

            if (_isWebViewReady && PreviewWebView.CoreWebView2 != null)
            {
                string payload = JsonSerializer.Serialize(
                    new WebViewCommandMessage { Action = "theme", Theme = isDark ? "dark" : "light" },
                    SessionJsonContext.Default.WebViewCommandMessage);
                PreviewWebView.CoreWebView2.PostWebMessageAsJson(payload);
            }
        }

        private void ApplyFocusModeToWebView(bool enabled)
        {
            if (_isWebViewReady && PreviewWebView.CoreWebView2 != null)
            {
                string payload = JsonSerializer.Serialize(
                    new WebViewCommandMessage { Action = "focus", Enabled = enabled },
                    SessionJsonContext.Default.WebViewCommandMessage);
                PreviewWebView.CoreWebView2.PostWebMessageAsJson(payload);
            }
        }

        #endregion

        #region Dark Code Editor, Line Numbers, Snippets & Debounced Rendering

        private void EditorTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(EditorTextBox);
            if (scrollViewer != null)
            {
                scrollViewer.ViewChanged += (_, _) =>
                {
                    LineNumbersScrollViewer.ChangeView(null, scrollViewer.VerticalOffset, null, true);
                };
            }
            UpdateLineNumbersAndStats();
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSuppressingEditorTextChange || _activeDocument == null)
            {
                return;
            }

            _activeDocument.Content = EditorTextBox.Text;
            UpdateLineNumbersAndStats();
            QueueDebouncedRender();
        }

        private void EditorTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateCursorPositionStatus();
        }

        private void EditorTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Tab)
            {
                e.Handled = true;
                InsertTextAtSelection("    ");
            }
        }

        private void UpdateLineNumbersAndStats()
        {
            string text = EditorTextBox.Text ?? string.Empty;
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            int lineCount = Math.Max(1, normalized.Count(c => c == '\n') + 1);

            var sb = new StringBuilder(lineCount * 4);
            for (int i = 1; i <= lineCount; i++)
            {
                if (i > 1)
                {
                    sb.Append('\n');
                }
                sb.Append(i);
            }
            LineNumbersTextBlock.Text = sb.ToString();

            int charCount = text.Length;
            int wordCount = string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

            StatusStatsText.Text = $"Words: {wordCount} | Chars: {charCount}";
            UpdateCursorPositionStatus();
        }

        private void UpdateCursorPositionStatus()
        {
            string text = EditorTextBox.Text ?? string.Empty;
            int selStart = Math.Clamp(EditorTextBox.SelectionStart, 0, text.Length);
            string upToCursor = text[..selStart].Replace("\r\n", "\n").Replace('\r', '\n');

            int line = upToCursor.Count(c => c == '\n') + 1;
            int lastNewLine = upToCursor.LastIndexOf('\n');
            int col = lastNewLine < 0 ? upToCursor.Length + 1 : upToCursor.Length - lastNewLine;

            StatusCursorText.Text = $"Ln {line}, Col {col}";
        }

        private void UpdateActiveDocumentHeaderAndStatus()
        {
            if (_activeDocument == null)
            {
                return;
            }

            ActiveDocHeaderPathText.Text = _activeDocument.DisplayTitle;
            StatusFilePathText.Text = string.IsNullOrWhiteSpace(_activeDocument.FilePath)
                ? $"{_activeDocument.DisplayTitle} (Unsaved Workspace)"
                : _activeDocument.FilePath + (_activeDocument.IsDirty ? " (Modified)" : "");
        }

        private void QueueDebouncedRender()
        {
            _renderDebounceTimer.Stop();
            _renderDebounceTimer.Start();
        }

        private async Task RenderMarkdownNowAsync()
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            string rawMarkdown = _activeDocument?.Content ?? EditorTextBox.Text ?? string.Empty;
            string payload = JsonSerializer.Serialize(
                new WebViewCommandMessage { Action = "render", Markdown = rawMarkdown },
                SessionJsonContext.Default.WebViewCommandMessage);
            PreviewWebView.CoreWebView2.PostWebMessageAsJson(payload);
            await Task.CompletedTask;
        }

        private void InsertTextAtSelection(string snippet, bool wrapSelection = false, string wrapPrefix = "", string wrapSuffix = "")
        {
            string current = EditorTextBox.Text ?? string.Empty;
            int start = Math.Clamp(EditorTextBox.SelectionStart, 0, current.Length);
            int length = Math.Clamp(EditorTextBox.SelectionLength, 0, current.Length - start);

            string replacement;
            if (wrapSelection)
            {
                string selectedText = length > 0 ? current.Substring(start, length) : "text";
                replacement = $"{wrapPrefix}{selectedText}{wrapSuffix}";
            }
            else
            {
                replacement = snippet;
            }

            EditorTextBox.Text = current[..start] + replacement + current[(start + length)..];
            EditorTextBox.SelectionStart = start + replacement.Length;
            EditorTextBox.SelectionLength = 0;
            EditorTextBox.Focus(FocusState.Programmatic);
            _ = RenderMarkdownNowAsync();
        }

        private void InsertBold_Click(object sender, RoutedEventArgs e)
            => InsertTextAtSelection("", wrapSelection: true, wrapPrefix: "**", wrapSuffix: "**");

        private void InsertItalic_Click(object sender, RoutedEventArgs e)
            => InsertTextAtSelection("", wrapSelection: true, wrapPrefix: "*", wrapSuffix: "*");

        private void InsertTableSnippet_Click(object sender, RoutedEventArgs e)
            => InsertTextAtSelection("\n| Header | Header |\n| :--- | :--- |\n| Cell | Cell |\n");

        private void InsertMermaidSnippet_Click(object sender, RoutedEventArgs e)
            => InsertTextAtSelection("\n```mermaid\ngraph TD\n  A --> B\n```\n");

        private void InsertPageBreakSnippet_Click(object sender, RoutedEventArgs e)
            => InsertTextAtSelection("\n<div style=\"page-break-after: always;\"></div>\n");

        private async void ClearEditor_Click(object sender, RoutedEventArgs e)
        {
            if (RootGrid.XamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Clear workspace?",
                Content = "Are you sure you want to clear all Markdown content in the current editor tab?",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                EditorTextBox.Text = string.Empty;
                await RenderMarkdownNowAsync();
            }
        }

        #endregion

        #region Bulk Update (.md) Implementation

        private async void SelectBulkFiles_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializePickerWithWindow(picker);
            picker.ViewMode = PickerViewMode.List;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".md");
            picker.FileTypeFilter.Add(".markdown");

            var newFiles = await picker.PickMultipleFilesAsync();
            if (newFiles == null || newFiles.Count == 0)
            {
                return;
            }

            if (_bulkFiles.Count + newFiles.Count > 20)
            {
                if (RootGrid.XamlRoot != null)
                {
                    var alert = new ContentDialog
                    {
                        XamlRoot = RootGrid.XamlRoot,
                        Title = "Bulk File Limit Reached",
                        Content = "Maximum 20 files allowed in total. Please remove some first.",
                        CloseButtonText = "OK"
                    };
                    await alert.ShowAsync();
                }
                return;
            }

            foreach (var file in newFiles)
            {
                var info = new FileInfo(file.Path);
                _bulkFiles.Add(new BulkFileItem
                {
                    FileName = file.Name,
                    FullPath = file.Path,
                    SizeBytes = info.Exists ? info.Length : 0
                });
            }

            RefreshBulkQueueState();
        }

        private void ClearBulkFiles_Click(object sender, RoutedEventArgs e)
        {
            _bulkFiles.Clear();
            RefreshBulkQueueState();
        }

        private void MoveBulkFileUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BulkFileItem item)
            {
                int idx = _bulkFiles.IndexOf(item);
                if (idx > 0)
                {
                    _bulkFiles.Move(idx, idx - 1);
                    RefreshBulkQueueState();
                }
            }
        }

        private void MoveBulkFileDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BulkFileItem item)
            {
                int idx = _bulkFiles.IndexOf(item);
                if (idx >= 0 && idx < _bulkFiles.Count - 1)
                {
                    _bulkFiles.Move(idx, idx + 1);
                    RefreshBulkQueueState();
                }
            }
        }

        private void RemoveBulkFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BulkFileItem item)
            {
                _bulkFiles.Remove(item);
                RefreshBulkQueueState();
            }
        }

        private void RefreshBulkQueueState()
        {
            for (int i = 0; i < _bulkFiles.Count; i++)
            {
                _bulkFiles[i].Index = i + 1;
                _bulkFiles[i].CanMoveUp = i > 0;
                _bulkFiles[i].CanMoveDown = i < _bulkFiles.Count - 1;
            }

            EmptyBulkListText.Visibility = _bulkFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProcessBulkBtn.IsEnabled = _bulkFiles.Count > 0 && _bulkFiles.Count <= 20;
            BulkCountStatusText.Text = $"{_bulkFiles.Count} / 20 files queued";
        }

        private async void ProcessBulkFiles_Click(object sender, RoutedEventArgs e)
        {
            if (_bulkFiles.Count == 0 || _bulkFiles.Count > 20)
            {
                return;
            }

            ProcessBulkBtn.IsEnabled = false;
            int fileCount = _bulkFiles.Count;

            try
            {
                var sb = new StringBuilder();
                for (int i = 0; i < _bulkFiles.Count; i++)
                {
                    var item = _bulkFiles[i];
                    string text = File.Exists(item.FullPath)
                        ? await File.ReadAllTextAsync(item.FullPath, Encoding.UTF8)
                        : string.Empty;

                    if (i > 0)
                    {
                        sb.Append("\n\n<div style=\"page-break-after: always;\"></div>\n\n");
                    }
                    sb.Append($"<!-- BEGIN FILE: {item.FileName} -->\n");
                    sb.Append(text);
                }

                string combinedContent = sb.ToString();
                string currentEditorText = EditorTextBox.Text ?? string.Empty;

                if (string.IsNullOrWhiteSpace(currentEditorText) ||
                    currentEditorText.Contains("Welcome to Markdown Pro", StringComparison.Ordinal))
                {
                    EditorTextBox.Text = combinedContent.TrimStart();
                }
                else
                {
                    EditorTextBox.Text = currentEditorText + "\n\n<div style=\"page-break-after: always;\"></div>\n\n" + combinedContent;
                }

                await RenderMarkdownNowAsync();
                _bulkFiles.Clear();
                RefreshBulkQueueState();
                SetActiveSection("editor");

                TriggerNotification($"Successfully appended {fileCount} files.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                TriggerNotification($"Bulk import error: {ex.Message}", InfoBarSeverity.Error);
                RefreshBulkQueueState();
            }
        }

        #endregion

        #region Help & Syntax and Mermaid Guide Interactive Copy Handlers

        private void CopyPageBreakBracket_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard("<div style=\"page-break-after: always;\"></div>");
            TriggerNotification("Page break HTML bracket copied to clipboard!", InfoBarSeverity.Success);
        }

        private void CopySyntaxRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string snippet)
            {
                CopyTextToClipboard(snippet);
                TriggerNotification($"Copied syntax: {snippet}", InfoBarSeverity.Success);
            }
        }

        private void CopyMermaidShape_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string snippet)
            {
                CopyTextToClipboard(snippet);
                TriggerNotification("Mermaid snippet copied to clipboard!", InfoBarSeverity.Success);
            }
        }

        private void CopyFlowchartTemplate_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard("graph TD\n  A[Start] --> B{Decision}\n  B -->|Yes| C[Success]\n  B -->|No| D[Retry]\n  style C fill:#d4edda,stroke:#28a745");
            TriggerNotification("Mermaid snippet copied to clipboard!", InfoBarSeverity.Success);
        }

        private void CopySequenceTemplate_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard("sequenceDiagram\n  Alice->>John: Hello John, how are you?\n  John-->>Alice: Great! Thanks.\n  Alice->>John: See you soon!");
            TriggerNotification("Mermaid snippet copied to clipboard!", InfoBarSeverity.Success);
        }

        private void CopyErTemplate_Click(object sender, RoutedEventArgs e)
        {
            CopyTextToClipboard("erDiagram\n  CUSTOMER ||--o{ ORDER : places\n  ORDER ||--|{ LINE_ITEM : contains\n  CUSTOMER {\n    string name\n    string email\n  }");
            TriggerNotification("Mermaid snippet copied to clipboard!", InfoBarSeverity.Success);
        }

        private static void CopyTextToClipboard(string text)
        {
            var pkg = new DataPackage();
            pkg.SetText(text);
            Clipboard.SetContent(pkg);
        }

        #endregion

        #region Export (.md, .html, .pdf, Print, Copy Rendered HTML)

        private async void ExportMarkdown_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            InitializePickerWithWindow(picker);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Markdown Document", new List<string> { ".md" });
            picker.SuggestedFileName = $"markdownpro-doc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.md";

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            await File.WriteAllTextAsync(file.Path, EditorTextBox.Text ?? string.Empty, Encoding.UTF8);
            TriggerNotification("Exported as MD", InfoBarSeverity.Success);
        }

        private async void ExportHtml_Click(object sender, RoutedEventArgs e)
        {
            await RenderMarkdownNowAsync();
            await Task.Delay(200);

            var picker = new FileSavePicker();
            InitializePickerWithWindow(picker);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("HTML Document", new List<string> { ".html" });
            picker.SuggestedFileName = $"markdownpro-doc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.html";

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            string htmlToSave = _lastRenderedInnerHtml;
            if (_isWebViewReady && PreviewWebView.CoreWebView2 != null)
            {
                try
                {
                    string title = _activeDocument?.Title ?? "Markdown Pro Document";
                    string jsonTitle = JsonSerializer.Serialize(title, SessionJsonContext.Default.String);
                    string jsonResult = await PreviewWebView.ExecuteScriptAsync($"getStandaloneHtml({jsonTitle})");
                    string? standalone = JsonSerializer.Deserialize(jsonResult, SessionJsonContext.Default.String);
                    if (!string.IsNullOrWhiteSpace(standalone))
                    {
                        htmlToSave = standalone;
                    }
                }
                catch
                {
                    // Fallback to inner HTML
                }
            }

            await File.WriteAllTextAsync(file.Path, htmlToSave, Encoding.UTF8);
            TriggerNotification("Exported as HTML", InfoBarSeverity.Success);
        }

        private async void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                TriggerNotification("Preview engine is still initializing.", InfoBarSeverity.Warning);
                return;
            }

            await RenderMarkdownNowAsync();
            await Task.Delay(300);

            var picker = new FileSavePicker();
            InitializePickerWithWindow(picker);
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", new List<string> { ".pdf" });
            picker.SuggestedFileName = $"markdownpro-doc-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.pdf";

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            try
            {
                var printSettings = PreviewWebView.CoreWebView2.Environment.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;

                bool ok = await PreviewWebView.CoreWebView2.PrintToPdfAsync(file.Path, printSettings);
                if (ok)
                {
                    TriggerNotification($"Exported PDF: {file.Name}", InfoBarSeverity.Success);
                }
                else
                {
                    TriggerNotification("PDF export failed.", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                TriggerNotification($"PDF export error: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private async void PrintDialog_Click(object sender, RoutedEventArgs e)
        {
            if (!_isWebViewReady || PreviewWebView.CoreWebView2 == null)
            {
                return;
            }

            await RenderMarkdownNowAsync();
            await Task.Delay(300);
            PreviewWebView.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }

        private async void CopyRenderedHtml_Click(object sender, RoutedEventArgs e)
        {
            await RenderMarkdownNowAsync();
            await Task.Delay(150);

            CopyTextToClipboard(_lastRenderedInnerHtml);
            TriggerNotification("HTML copied to clipboard!", InfoBarSeverity.Success);
        }

        #endregion

        #region Keyboard Accelerators, Notifications & Window Helpers

        private void OnNewFileAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            CreateNewUntitledDocument();
        }

        private async void OnOpenFileAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await PickAndOpenFileAsync();
        }

        private async void OnOpenFolderAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            await PickAndOpenFolderAsync();
        }

        private async void OnSaveFileAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (_activeDocument != null)
            {
                await SaveDocumentInternalAsync(_activeDocument, forceSaveAs: false);
            }
        }

        private async void OnSaveAsFileAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            if (_activeDocument != null)
            {
                await SaveDocumentInternalAsync(_activeDocument, forceSaveAs: true);
            }
        }

        private void OnPrintAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            PrintDialog_Click(this, new RoutedEventArgs());
        }

        private void OnBoldAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            InsertBold_Click(this, new RoutedEventArgs());
        }

        private void OnItalicAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            InsertItalic_Click(this, new RoutedEventArgs());
        }

        private void TriggerNotification(string message, InfoBarSeverity severity)
        {
            AppInfoBar.Severity = severity;
            AppInfoBar.Message = message;
            AppInfoBar.IsOpen = true;
            _infoBarTimer.Stop();
            _infoBarTimer.Start();
        }

        private void InitializePickerWithWindow(object picker)
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        private void SaveSession()
        {
            _session.LastFolderPath = _rootOpenedFolderPath ?? _currentFolderPath;
            _session.OpenFilePaths = _openDocuments
                .Where(d => !string.IsNullOrWhiteSpace(d.FilePath) && File.Exists(d.FilePath))
                .Select(d => d.FilePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _session.ActiveFilePath = _activeDocument?.FilePath;
            _session.PreviewTheme = _currentPreviewTheme;
            _session.PreviewLayout = _currentPreviewLayout;

            SessionService.Save(_session);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            SaveSession();
        }

        #endregion

        #region Updates, License & About

        private static string GetLicenseTermsText()
        {
            return """
                MIT License & Terms of Use

                Copyright (c) 2026 KevinK56 / Star Systems

                Permission is hereby granted, free of charge, to any person obtaining a copy
                of this software and associated documentation files ("Markdown Pro"), to deal
                in the Software without restriction, including without limitation the rights
                to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
                copies of the Software, and to permit persons to whom the Software is
                furnished to do so, subject to the following conditions:

                The above copyright notice and this permission notice shall be included in all
                copies or substantial portions of the Software.

                TERMS OF USE & PRIVACY:
                1. Local & Offline Processing: All Markdown parsing, Mermaid diagram rendering,
                   bulk file merging, and PDF generation run 100% locally on your device.
                2. GitHub Update Service: When "Check for Updates on Startup" is enabled, the
                   application queries the public GitHub Releases API (KevinK56/MarkdownPro)
                   to check for newer versions. You can disable this anytime under Preferences.
                3. No Warranty: THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
                   EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
                   MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
                """;
        }

        private async Task PromptLicenseAcceptanceOnFirstRunAsync()
        {
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = GetLicenseTermsText(),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                }
            };

            var dialog = new ContentDialog
            {
                Title = "Markdown Pro — License & Terms of Use",
                Content = scrollViewer,
                PrimaryButtonText = "I Accept",
                CloseButtonText = "Decline & Exit",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _session.HasAcceptedLicense = true;
                SaveSession();
            }
            else
            {
                Application.Current.Exit();
            }
        }

        private async void ViewLicenseTerms_Click(object sender, RoutedEventArgs e)
        {
            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = GetLicenseTermsText(),
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                }
            };

            var dialog = new ContentDialog
            {
                Title = "License & Terms of Use",
                Content = scrollViewer,
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void AboutApp_Click(object sender, RoutedEventArgs e)
        {
            string version = UpdateService.GetCurrentVersionString();
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = $"Markdown Pro v{version}",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Dual-pane Markdown & Mermaid v11.4.0 Authoring Environment (100% Offline Engine).",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"GitHub Repository: https://github.com/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}",
                FontSize = 12,
                Opacity = 0.7,
                IsTextSelectionEnabled = true
            });

            var dialog = new ContentDialog
            {
                Title = "About Markdown Pro",
                Content = panel,
                PrimaryButtonText = "Check for Updates",
                SecondaryButtonText = "Open GitHub Repo",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                CheckForUpdates_Click(this, new RoutedEventArgs());
            }
            else if (result == ContentDialogResult.Secondary)
            {
                UpdateService.OpenUrlInBrowser($"https://github.com/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}");
            }
        }

        private void ToggleCheckUpdatesStartup_Click(object sender, RoutedEventArgs e)
        {
            _session.CheckForUpdatesOnStartup = MenuCheckUpdatesStartup.IsChecked;
            SaveSession();
            TriggerNotification(
                _session.CheckForUpdatesOnStartup
                    ? "Automatic startup update checks enabled."
                    : "Automatic startup update checks disabled.",
                InfoBarSeverity.Informational);
        }

        private async Task CheckForUpdatesInBackgroundAsync()
        {
            try
            {
                await Task.Delay(2500);
                var update = await UpdateService.CheckForUpdateAsync();
                if (update != null)
                {
                    DispatcherQueue.TryEnqueue(() => ShowUpdateBanner(update));
                }
            }
            catch
            {
                // Silent failure on background startup check
            }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            TriggerNotification("Checking GitHub Releases for updates...", InfoBarSeverity.Informational);

            try
            {
                var update = await UpdateService.CheckForUpdateAsync();
                if (update != null)
                {
                    ShowUpdateBanner(update);
                    TriggerNotification($"Update {update.TagName} is available!", InfoBarSeverity.Success);
                }
                else
                {
                    TriggerNotification(
                        $"You are running the latest version (v{UpdateService.GetCurrentVersionString()}).",
                        InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                TriggerNotification($"Could not check for updates: {ex.Message}", InfoBarSeverity.Warning);
            }
        }

        private void ShowUpdateBanner(UpdateInfo update)
        {
            _availableUpdate = update;
            string sizeInfo = update.InstallerSize > 0
                ? $" ({update.InstallerSize / (1024.0 * 1024.0):F1} MB)"
                : string.Empty;

            UpdateBannerText.Text =
                $"New update available: {update.ReleaseTitle} ({update.TagName}){sizeInfo} — Current: v{UpdateService.GetCurrentVersionString()}";
            BtnDownloadUpdate.Content = !string.IsNullOrEmpty(update.InstallerDownloadUrl)
                ? "Download & Install"
                : "Download from GitHub";
            BtnDownloadUpdate.IsEnabled = true;
            UpdateDownloadProgress.Visibility = Visibility.Collapsed;
            UpdateBannerBorder.Visibility = Visibility.Visible;
        }

        private async void DownloadAndInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null || _isDownloadingUpdate)
            {
                return;
            }

            if (string.IsNullOrEmpty(_availableUpdate.InstallerDownloadUrl))
            {
                UpdateService.OpenUrlInBrowser(_availableUpdate.ReleasePageUrl);
                return;
            }

            _isDownloadingUpdate = true;
            BtnDownloadUpdate.IsEnabled = false;
            UpdateDownloadProgress.Value = 0;
            UpdateDownloadProgress.Visibility = Visibility.Visible;

            try
            {
                var progress = new Progress<double>(pct =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        UpdateDownloadProgress.Value = pct;
                        BtnDownloadUpdate.Content = $"Downloading {pct:F0}%...";
                    });
                });

                string installerPath = await UpdateService.DownloadInstallerAsync(
                    _availableUpdate.InstallerDownloadUrl,
                    progress);

                BtnDownloadUpdate.Content = "Launching Installer...";
                SaveSession();
                UpdateService.LaunchInstallerAndShutdown(installerPath);
            }
            catch (Exception ex)
            {
                _isDownloadingUpdate = false;
                BtnDownloadUpdate.IsEnabled = true;
                BtnDownloadUpdate.Content = "Download & Install";
                UpdateDownloadProgress.Visibility = Visibility.Collapsed;
                TriggerNotification($"Update download failed: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        private void ViewReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate != null && !string.IsNullOrEmpty(_availableUpdate.ReleasePageUrl))
            {
                UpdateService.OpenUrlInBrowser(_availableUpdate.ReleasePageUrl);
            }
            else
            {
                UpdateService.OpenUrlInBrowser($"https://github.com/{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}/releases");
            }
        }

        private void DismissUpdateBanner_Click(object sender, RoutedEventArgs e)
        {
            UpdateBannerBorder.Visibility = Visibility.Collapsed;
        }

        #endregion
    }
}
