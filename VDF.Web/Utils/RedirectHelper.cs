namespace VDF.Web.Utils;

/// <summary>
/// Validates redirect URLs to prevent open redirect attacks.
/// </summary>
static class RedirectHelper {
	/// <summary>
	/// Returns true only if <paramref name="url"/> is a safe, local-relative URL
	/// that cannot redirect the user to an external site.
	/// </summary>
	internal static bool IsValidReturnUrl(string? url) {
		if (string.IsNullOrEmpty(url))
			return false;
		if (!url.StartsWith('/'))
			return false;
		if (url.StartsWith("//", StringComparison.Ordinal))
			return false;
		if (url.Contains("://", StringComparison.Ordinal))
			return false;
		return true;
	}

	/// <summary>
	/// Returns <paramref name="url"/> if it is a valid local URL, otherwise returns "/".
	/// </summary>
	internal static string SafeReturnUrl(string? url) => IsValidReturnUrl(url) ? url! : "/";
}
