using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using VDF.GUI.Data;

namespace VDF.GUI.Utils {

	/// <summary>
	/// Manages application-wide theme switching between dark and light modes.
	/// Persists the user's preference via SettingsFile.
	/// </summary>
	public static class ThemeManager {

		/// <summary>
		/// Apply the current theme from settings to the application.
		/// Call once at startup after settings are loaded.
		/// </summary>
		public static void ApplyTheme() {
			SetTheme(SettingsFile.Instance.DarkMode);
		}

		/// <summary>
		/// Set the application theme to dark or light mode.
		/// </summary>
		/// <param name="isDark">True for dark theme, false for light theme.</param>
		public static void SetTheme(bool isDark) {
			var app = Application.Current;
			if (app == null) return;

			app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;

			// Persist the preference
			SettingsFile.Instance.DarkMode = isDark;
		}

		/// <summary>
		/// Toggle between dark and light themes.
		/// </summary>
		public static void ToggleTheme() {
			SetTheme(!SettingsFile.Instance.DarkMode);
		}

		/// <summary>
		/// Apply theme to a specific window (needed for per-window theme in some scenarios).
		/// </summary>
		public static void SetWindowTheme(Window window, bool isDark) {
			window.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
		}
	}
}
