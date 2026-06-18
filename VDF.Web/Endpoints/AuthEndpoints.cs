using VDF.Web.ApiModels;
using VDF.Web.Auth;
using VDF.Web.Services;

namespace VDF.Web.Endpoints;

static class AuthEndpoints {
	public static WebApplication MapAuthApi(this WebApplication app) {
		var group = app.MapGroup("/api/auth");
		group.RequireAuthorization();

		group.MapGet("/users", (UserStore userStore) => {
			var users = userStore.GetAllUsers();
			return Results.Ok(ApiResponse.Ok(users));
		});

		group.MapPost("/users", (UserStore userStore, CreateUserRequest req) => {
			if (string.IsNullOrWhiteSpace(req.Username))
				return Results.Json(ApiResponse.Fail("username_required", "validation_error"), statusCode: 400);

			if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
				return Results.Json(ApiResponse.Fail("password_min_length_6", "validation_error"), statusCode: 400);

			if (userStore.UserExists(req.Username))
				return Results.Json(ApiResponse.Fail("username_already_exists", "conflict"), statusCode: 409);

			Role role = Role.Viewer;
			if (!string.IsNullOrWhiteSpace(req.Role))
				role = RoleExtensions.FromClaimValue(req.Role);

			userStore.CreateUser(req.Username, req.Password, role);
			return Results.Ok(ApiResponse.Ok(new { username = req.Username, role = role.ToString() }));
		});

		group.MapPut("/users/{username}/role", (UserStore userStore, string username, UpdateRoleRequest req) => {
			if (!userStore.UserExists(username))
				return Results.NotFound(ApiResponse.Fail("user_not_found", "not_found"));

			Role role = RoleExtensions.FromClaimValue(req.Role);
			userStore.UpdateRole(username, role);
			return Results.Ok(ApiResponse.Ok(new { username, role = role.ToString() }));
		});

		group.MapDelete("/users/{username}", (UserStore userStore, string username) => {
			if (!userStore.UserExists(username))
				return Results.NotFound(ApiResponse.Fail("user_not_found", "not_found"));

			if (string.Equals(username, "admin", StringComparison.OrdinalIgnoreCase))
				return Results.Json(ApiResponse.Fail("cannot_delete_admin", "forbidden"), statusCode: 403);

			userStore.DeleteUser(username);
			return Results.Ok(ApiResponse.Ok(new { deleted = true }));
		});

		group.MapGet("/audit", (AuditService auditService, int? count) => {
			var entries = auditService.GetRecent(count ?? 100);
			return Results.Ok(ApiResponse.Ok(entries));
		});

		return app;
	}
}

public sealed class CreateUserRequest {
	public string Username { get; set; } = string.Empty;
	public string Password { get; set; } = string.Empty;
	public string? Role { get; set; }
}

public sealed class UpdateRoleRequest {
	public string Role { get; set; } = string.Empty;
}
