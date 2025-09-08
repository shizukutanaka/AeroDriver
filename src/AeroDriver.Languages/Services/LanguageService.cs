using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Languages.Services
{
    public interface ILanguageService
    {
        string GetString(string key);
        string GetString(string key, params object[] args);
        void SetLanguage(string languageCode);
        string GetCurrentLanguage();
        IEnumerable<LanguageInfo> GetSupportedLanguages();
    }

    public class LanguageService : ILanguageService
    {
        private readonly ILogger<LanguageService> _logger;
        private ResourceManager _resourceManager;
        private CultureInfo _currentCulture;

        private static readonly Dictionary<string, LanguageInfo> SupportedLanguages = new()
        {
            ["en-US"] = new("en-US", "English", "English"),
            ["ja-JP"] = new("ja-JP", "Japanese", "日本語"),
            ["zh-CN"] = new("zh-CN", "Chinese (Simplified)", "简体中文"),
            ["ko-KR"] = new("ko-KR", "Korean", "한국어"),
            ["de-DE"] = new("de-DE", "German", "Deutsch"),
            ["es-ES"] = new("es-ES", "Spanish", "Español"),
            ["fr-FR"] = new("fr-FR", "French", "Français"),
            ["it-IT"] = new("it-IT", "Italian", "Italiano"),
            ["pt-BR"] = new("pt-BR", "Portuguese (Brazil)", "Português"),
            ["ru-RU"] = new("ru-RU", "Russian", "Русский")
        };

        public LanguageService(ILogger<LanguageService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            InitializeLanguageService();
        }

        private void InitializeLanguageService()
        {
            try
            {
                _resourceManager = new ResourceManager("AeroDriver.Languages.Resources.Strings", typeof(LanguageService).Assembly);
                
                var systemLanguage = GetSystemLanguage();
                _currentCulture = SupportedLanguages.ContainsKey(systemLanguage)
                    ? new CultureInfo(systemLanguage)
                    : new CultureInfo("en-US");

                _logger.LogInformation("Language service initialized with culture: {Culture}", _currentCulture.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize language service, falling back to en-US");
                _currentCulture = new CultureInfo("en-US");
            }
        }

        public string GetString(string key)
        {
            try
            {
                var value = _resourceManager.GetString(key, _currentCulture);
                return value ?? key; // Fallback to key if not found
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get localized string for key: {Key}", key);
                return key;
            }
        }

        public string GetString(string key, params object[] args)
        {
            try
            {
                var format = GetString(key);
                return string.Format(_currentCulture, format, args);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to format localized string for key: {Key}", key);
                return key;
            }
        }

        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                _logger.LogWarning("Attempted to set null or empty language code");
                return;
            }

            if (!SupportedLanguages.ContainsKey(languageCode))
            {
                _logger.LogWarning("Unsupported language code: {LanguageCode}", languageCode);
                return;
            }

            try
            {
                _currentCulture = new CultureInfo(languageCode);
                _logger.LogInformation("Language changed to: {Language}", languageCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set language to: {LanguageCode}", languageCode);
            }
        }

        public string GetCurrentLanguage()
        {
            return _currentCulture.Name;
        }

        public IEnumerable<LanguageInfo> GetSupportedLanguages()
        {
            return SupportedLanguages.Values;
        }

        private static string GetSystemLanguage()
        {
            try
            {
                var systemCulture = CultureInfo.CurrentUICulture.Name;
                return SupportedLanguages.ContainsKey(systemCulture) ? systemCulture : "en-US";
            }
            catch
            {
                return "en-US";
            }
        }
    }

    public record LanguageInfo(string Code, string EnglishName, string NativeName);
}