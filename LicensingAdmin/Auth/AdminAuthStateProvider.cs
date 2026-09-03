using LicensingCore.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace LicensingAdmin.Auth;

/// <summary>
/// Server-side Blazor auth state provider that re-checks the signed-in admin against the
/// database every <see cref="RevalidationInterval"/>. If the account has been deactivated
/// or its role changed since the cookie was issued, the circuit's principal is dropped so
/// the UI can no longer act with stale privileges.
/// </summary>
public sealed class AdminAuthStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private readonly IServiceScopeFactory _scopeFactory;

    public AdminAuthStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopeFactory)
        : base(loggerFactory) => _scopeFactory = scopeFactory;

    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User;
        var email = user.FindFirst(ClaimTypes.Name)?.Value;
        var role = user.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            return false;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IAdminUserLookup>();
        var admin = await lookup.FindByEmailAsync(email, cancellationToken);

        return admin is { IsActive: true } && admin.Role.ToString() == role;
    }
}
