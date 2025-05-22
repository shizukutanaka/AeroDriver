using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Threading;
using Microsoft.Extensions.Logging;
using AeroDriver.Languages.Resources;

namespace AeroDriver.Languages.Services
{
    public interface ILanguageService
    {
        /// <summary>
        /// Gets the string for the current culture
        /// </summary>
        string GetString(string name);

        /// <summary>
        /// Gets the string for the specified culture
        /// </summary>
        string GetString(string name, CultureInfo culture);

        /// <summary>
        /// Gets the formatted string for the current culture
        /// </summary>
        string GetString(string name, params object[] args);

        /// <summary>
        /// Gets the current culture
        /// </summary>
        CultureInfo CurrentCulture { get; }

        /// <summary>
        /// Changes the current culture
        /// </summary>
        void SetCulture(CultureInfo culture);

        /// <summary>
        /// Gets all supported cultures
        /// </summary>
        IReadOnlyList<CultureInfo> SupportedCultures { get; }
    }


    public class LanguageService : ILanguageService, IDisposable
    {
        private readonly ILogger<LanguageService> _logger;
        private readonly ResourceManager _resourceManager;
        private CultureInfo _currentCulture;
        private bool _disposed = false;

        private static readonly Lazy<IReadOnlyList<CultureInfo>> _supportedCultures = new Lazy<IReadOnlyList<CultureInfo>>(() =>
            new List<CultureInfo>
            {
                new CultureInfo("en-US"), // English (United States)
                new CultureInfo("ja-JP"), // Japanese (Japan)
                new CultureInfo("zh-CN"), // Chinese (Simplified, China)
                new CultureInfo("ko-KR"), // Korean (Korea)
                new CultureInfo("fr-FR"), // French (France)
                new CultureInfo("es-ES"), // Spanish (Spain)
                new CultureInfo("de-DE"), // German (Germany)
                new CultureInfo("it-IT"), // Italian (Italy)
                new CultureInfo("pt-BR"), // Portuguese (Brazil)
                new CultureInfo("ru-RU"), // Russian (Russia)
            }.AsReadOnly()
        );

        public LanguageService(ILogger<LanguageService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resourceManager = new ResourceManager("AeroDriver.Languages.Resources.Strings", typeof(LanguageService).Assembly);
            _currentCulture = Thread.CurrentThread.CurrentUICulture;

            // Verify the current culture is supported, fallback to en-US if not
            if (!SupportedCultures.Contains(_currentCulture))
            {
                _currentCulture = new CultureInfo("en-US");
                Thread.CurrentThread.CurrentUICulture = _currentCulture;
                _logger.LogInformation("Unsupported culture '{0}' detected, falling back to 'en-US'", _currentCulture.Name);
            }

            _logger.LogInformation("Language service initialized with culture: {Culture}", _currentCulture.Name);
        }

        public string GetString(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Resource name cannot be null or empty", nameof(name));

            try
            {
                return _resourceManager.GetString(name, _currentCulture) ?? $"[{name}]";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resource string: {ResourceName}", name);
                return $"[{name}]";
            }
        }

        public string GetString(string name, CultureInfo culture)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Resource name cannot be null or empty", nameof(name));

            culture ??= _currentCulture;

            try
            {
                return _resourceManager.GetString(name, culture) ?? $"[{name}]";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resource string: {ResourceName} for culture: {Culture}", name, culture.Name);
                return $"[{name}]";
            }
        }

        public string GetString(string name, params object[] args)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Resource name cannot be null or empty", nameof(name));

            try
            {
                string format = _resourceManager.GetString(name, _currentCulture);
                return string.Format(format ?? $"[{name}]", args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting formatted resource string: {ResourceName}", name);
                return $"[{name}]";
            }
        }

        public CultureInfo CurrentCulture => _currentCulture;

        public IReadOnlyList<CultureInfo> SupportedCultures => _supportedCultures.Value;

        public void SetCulture(CultureInfo culture)
        {
            if (culture == null)
                throw new ArgumentNullException(nameof(culture));

            if (!_supportedCultures.Value.Contains(culture))
            {
                _logger.LogWarning("Culture '{0}' is not supported, falling back to 'en-US'", culture.Name);
                culture = new CultureInfo("en-US");
            }

            if (_currentCulture.Name != culture.Name)
            {
                _currentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                _logger.LogInformation("Culture changed to: {Culture}", culture.Name);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _resourceManager.ReleaseAllResources();
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
