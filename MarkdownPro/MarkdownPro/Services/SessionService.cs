using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkdownPro.Services
{
    public class SessionState
    {
        public string? LastFolderPath { get; set; }
        public List<string> OpenFilePaths { get; set; } = new();
        public string? ActiveFilePath { get; set; }
        public string PreviewTheme { get; set; } = "light";
        public string PreviewLayout { get; set; } = "editor"; // "editor", "split", "full"
        public bool IsSidebarOpen { get; set; } = true;
        public bool SuppressDefaultAppPrompt { get; set; }
        public bool CheckForUpdatesOnStartup { get; set; } = true;
        public bool HasAcceptedLicense { get; set; }
    }

    public class WebViewCommandMessage
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("markdown")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Markdown { get; set; }

        [JsonPropertyName("theme")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Theme { get; set; }

        [JsonPropertyName("enabled")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Enabled { get; set; }
    }

    [JsonSerializable(typeof(SessionState))]
    [JsonSerializable(typeof(WebViewCommandMessage))]
    [JsonSerializable(typeof(string))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class SessionJsonContext : JsonSerializerContext
    {
    }

    public static class SessionService
    {
        private static readonly string SessionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarkdownPro");

        private static readonly string SessionFilePath = Path.Combine(SessionDirectory, "session.json");

        public static SessionState Load()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    string json = File.ReadAllText(SessionFilePath);
                    var state = JsonSerializer.Deserialize(json, SessionJsonContext.Default.SessionState);
                    if (state != null)
                    {
                        if (state.OpenFilePaths != null)
                        {
                            state.OpenFilePaths.RemoveAll(p =>
                                string.IsNullOrWhiteSpace(p) ||
                                p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                                p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                        }
                        if (!string.IsNullOrWhiteSpace(state.ActiveFilePath) &&
                            (state.ActiveFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                             state.ActiveFilePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                        {
                            state.ActiveFilePath = null;
                        }
                        return state;
                    }
                }
            }
            catch
            {
                // Ignore corrupted session files and return default state
            }

            return new SessionState();
        }

        public static void Save(SessionState state)
        {
            try
            {
                Directory.CreateDirectory(SessionDirectory);
                string json = JsonSerializer.Serialize(state, SessionJsonContext.Default.SessionState);
                File.WriteAllText(SessionFilePath, json);
            }
            catch
            {
                // Ignore disk write failures
            }
        }
    }
}
