using Microsoft.Extensions.Logging.Abstractions;
using VDF.Web.Services;

namespace VDF.Web.Tests;

public class JwtServiceTests {
	private readonly JwtService _jwtService;

	public JwtServiceTests() {
		var logger = NullLogger<JwtService>.Instance;
		_jwtService = new JwtService(logger);
	}

	[Fact]
	public void GenerateToken_ReturnsNonEmptyToken() {
		// Act
		var token = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));

		// Assert
		Assert.False(string.IsNullOrEmpty(token));
	}

	[Fact]
	public void GenerateToken_ContainsCorrectClaims() {
		// Act
		var token = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));
		var principal = _jwtService.ValidateToken(token);

		// Assert
		Assert.NotNull(principal);
		Assert.Equal("user1", principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
		Assert.Equal("admin", principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value);
	}

	[Fact]
	public void ValidateToken_WithValidToken_ReturnsPrincipal() {
		// Arrange
		var token = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));

		// Act
		var principal = _jwtService.ValidateToken(token);

		// Assert
		Assert.NotNull(principal);
		Assert.True(principal.Identity?.IsAuthenticated);
	}

	[Fact]
	public void ValidateToken_WithInvalidToken_ReturnsNull() {
		// Act
		var principal = _jwtService.ValidateToken("invalid-token");

		// Assert
		Assert.Null(principal);
	}

	[Fact]
	public void ValidateToken_WithTamperedSignature_ReturnsNull() {
		// Arrange
		var token = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));
		// Tamper with the signature by changing the last character
		var parts = token.Split('.');
		parts[2] = parts[2][..^1] + "X";
		var tamperedToken = string.Join(".", parts);

		// Act
		var principal = _jwtService.ValidateToken(tamperedToken);

		// Assert
		Assert.Null(principal);
	}

	[Fact]
	public void ValidateToken_WithTamperedToken_ReturnsNull() {
		// Arrange
		var token = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));
		var tamperedToken = token.Substring(0, token.Length - 5) + "XXXXX";

		// Act
		var principal = _jwtService.ValidateToken(tamperedToken);

		// Assert
		Assert.Null(principal);
	}

	[Fact]
	public void GetSigningKey_ReturnsNonEmptyKey() {
		// Act
		var key = _jwtService.GetSigningKey();

		// Assert
		Assert.NotNull(key);
		Assert.True(key.Key.Length >= 32);
	}

	[Fact]
	public void GenerateToken_DifferentCallsProduceDifferentTokens() {
		// Act
		var token1 = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));
		var token2 = _jwtService.GenerateToken("user1", "admin", TimeSpan.FromMinutes(15));

		// Assert
		Assert.NotEqual(token1, token2);
	}
}
