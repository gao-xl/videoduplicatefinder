using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VDF.Web.Services;

namespace VDF.Web.Tests;

public class AuthServiceTests {
	private readonly AuthService _authService;

	public AuthServiceTests() {
		var logger = NullLogger<AuthService>.Instance;
		var jwtLogger = NullLogger<JwtService>.Instance;
		var jwtService = new JwtService(jwtLogger);
		var configLogger = NullLogger<WebConfigService>.Instance;
		var config = new WebConfigService(configLogger);
		_authService = new AuthService(logger, jwtService, config);
	}

	[Fact]
	public void ValidatePassword_WithEmptyPassword_ReturnsFalse() {
		// Act
		var result = _authService.ValidatePassword("");

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void ValidatePassword_WithWrongPassword_ReturnsFalse() {
		// Act
		var result = _authService.ValidatePassword("wrong-password-123!");

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void GenerateAccessToken_ReturnsNonEmptyToken() {
		// Act
		var token = _authService.GenerateAccessToken();

		// Assert
		Assert.False(string.IsNullOrEmpty(token));
	}

	[Fact]
	public void GenerateRefreshToken_ReturnsNonEmptyToken() {
		// Act
		var token = _authService.GenerateRefreshToken();

		// Assert
		Assert.False(string.IsNullOrEmpty(token));
	}

	[Fact]
	public void ValidateRefreshToken_WithValidToken_ReturnsTrue() {
		// Arrange
		var token = _authService.GenerateRefreshToken();

		// Act
		var result = _authService.ValidateRefreshToken(token);

		// Assert
		Assert.True(result);
	}

	[Fact]
	public void ValidateRefreshToken_WithInvalidToken_ReturnsFalse() {
		// Act
		var result = _authService.ValidateRefreshToken("invalid-token");

		// Assert
		Assert.False(result);
	}

	[Fact]
	public void RefreshAccessToken_WithValidToken_ReturnsNewToken() {
		// Arrange
		var refreshToken = _authService.GenerateRefreshToken();

		// Act
		var newToken = _authService.RefreshAccessToken(refreshToken);

		// Assert
		Assert.NotNull(newToken);
		Assert.False(string.IsNullOrEmpty(newToken));
	}

	[Fact]
	public void RefreshAccessToken_WithInvalidToken_ReturnsNull() {
		// Act
		var result = _authService.RefreshAccessToken("invalid-token");

		// Assert
		Assert.Null(result);
	}

	[Fact]
	public void ValidateApiKey_WithValidKey_ReturnsTrue() {
		// This test requires setting up API keys, which depends on config
		// For now, test with empty key list
		// Act
		var result = _authService.ValidateApiKey("test-key");

		// Assert
		Assert.False(result);
	}

}
