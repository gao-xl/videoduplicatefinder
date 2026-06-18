namespace VDF.Web.Auth;

public enum Role {
	Admin = 0,
	Operator = 1,
	Viewer = 2,
}

public static class RoleExtensions {
	public static string ToClaimValue(this Role role) => role.ToString().ToLowerInvariant();

	public static Role FromClaimValue(string? value) => value?.ToLowerInvariant() switch {
		"admin" => Role.Admin,
		"operator" => Role.Operator,
		"viewer" => Role.Viewer,
		_ => Role.Viewer,
	};

	public static bool CanScan(this Role role) => role <= Role.Operator;
	public static bool CanDelete(this Role role) => role <= Role.Operator;
	public static bool CanModifySettings(this Role role) => role == Role.Admin;
	public static bool CanManageUsers(this Role role) => role == Role.Admin;
}
