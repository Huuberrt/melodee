using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Melodee.Common.Constants;
using Microsoft.IdentityModel.Tokens;

namespace Melodee.Blazor.Services;

/// <summary>
///     Store and manage the current user's authentication state as a browser Session JWT and in Server Side Blazor
/// </summary>
public class AuthService(
    ILocalStorageService localStorageService,
    IConfiguration configuration)
    : IAuthService
{
    private const string AuthTokenName = "melodee_auth_token";
    private ClaimsPrincipal? _currentUser;
    private bool _tokenValidated = false;

    public event Action<ClaimsPrincipal>? UserChanged;

    public ClaimsPrincipal CurrentUser
    {
        get => _currentUser ?? new ClaimsPrincipal();
        set
        {
            _currentUser = value;

            if (UserChanged is not null)
            {
                UserChanged(_currentUser);
            }
        }
    }

    public bool IsAdmin => CurrentUser.IsInRole(RoleNameRegistry.Administrator);

    public bool IsLoggedIn => CurrentUser.Identity?.IsAuthenticated ?? false;

    public async Task LogoutAsync()
    {
        CurrentUser = new ClaimsPrincipal();
        _tokenValidated = false; // Reset validation state on logout
        await localStorageService.RemoveItemAsync(AuthTokenName);
    }


    /// <summary>
    ///     If the user somehow loses their server session, this method will attempt to restore the state from the JWT in the
    ///     browser session
    /// </summary>
    /// <returns>True if the state was restored</returns>
    public async Task<bool> GetStateFromTokenAsync()
    {
        var result = false;
        var authToken = await localStorageService.GetItemAsStringAsync(AuthTokenName);
        var identity = new ClaimsIdentity();
        if (!string.IsNullOrEmpty(authToken))
        {
            try
            {
                //Ensure the JWT is valid
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(configuration.GetSection("MelodeeAuthSettings:Token").Value!);

                tokenHandler.ValidateToken(authToken, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                }, out var validatedToken);

                var jwtToken = (JwtSecurityToken)validatedToken;
                identity = new ClaimsIdentity(jwtToken.Claims, "jwt");
                result = true;
            }
            catch
            {
                await localStorageService.RemoveItemAsync(AuthTokenName);
                identity = new ClaimsIdentity();
            }
        }

        var user = new ClaimsPrincipal(identity);
        CurrentUser = user;
        return result;
    }


    /// <summary>
    ///     Ensures user is authenticated by validating cached state or token. Prevents duplicate validation calls.
    /// </summary>
    /// <returns>True if user is authenticated</returns>
    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (!_tokenValidated && !IsLoggedIn)
        {
            _tokenValidated = true;
            return await GetStateFromTokenAsync();
        }
        return IsLoggedIn;
    }

    public async Task Login(ClaimsPrincipal user, bool? doRememberMe = null)
    {
        CurrentUser = user;
        _tokenValidated = true; // Mark as validated since we're setting a valid user
        var tokenEncryptionKey = configuration.GetSection("MelodeeAuthSettings:Token").Value!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenEncryptionKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);
        var tokenHoursString = configuration.GetSection("MelodeeAuthSettings:TokenHours").Value;
        int.TryParse(tokenHoursString, out var tokenHours);
        var token = new JwtSecurityToken(
            claims: user.Claims,
            expires: DateTime.Now.AddHours(tokenHours),
            signingCredentials: creds);
        var jwt = new JwtSecurityTokenHandler().WriteToken(token);
        await localStorageService.SetItemAsStringAsync(AuthTokenName, jwt);
    }
}
