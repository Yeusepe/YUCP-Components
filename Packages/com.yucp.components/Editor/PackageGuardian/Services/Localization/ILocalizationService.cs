namespace YUCP.Components.PackageGuardian.Editor.Services.Localization
{
    /// <summary>
    /// Interface for localization service.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Get localized string by key.
        /// </summary>
        string GetString(string key);
        
        /// <summary>
        /// Get current language.
        /// </summary>
        string CurrentLanguage { get; }
    }
}

