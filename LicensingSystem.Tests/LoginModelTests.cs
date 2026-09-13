using LicensingAdmin.Auth;
using LicensingAdmin.Pages.Account;
using LicensingCore.Data;
using LicensingCore.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LicensingSystem.Tests;

/// <summary>
/// Deterministic unit tests for <see cref="LoginModel"/> (E2-T2 criteria 4 and 5's
/// failure half). No database and no host: a real <see cref="AdminCredentialService"/>
/// is driven over a fake <see cref="IAdminUserLookup"/>, and a real
/// <see cref="UrlHelper"/> (whose <c>IsLocalUrl</c> is a pure string check) backs the
/// page's <c>Url</c> property.
///
/// Surface-area note: <see cref="LoginModel"/>'s <c>Local(returnUrl)</c> reducer is
/// <c>private</c>. Rather than widen it to <c>internal</c> + <c>InternalsVisibleTo</c>,
/// these tests exercise it through the two public entry points that call it
/// (<see cref="LoginModel.OnGet"/> and <see cref="LoginModel.OnPostAsync"/>) and assert
/// on the observable <see cref="LoginModel.ReturnUrl"/>. That keeps production code
/// untouched. The GET pipeline / hidden-field angle is covered end to end in
/// <c>AuthorizationPipelineTests</c>.
/// </summary>
public class LoginModelTests
{
    // ---- test doubles -----------------------------------------------------------

    private sealed class FakeAdminUserLookup(AdminUser? user) : IAdminUserLookup
    {
        public string? LastEmailQueried;

        public Task<AdminUser?> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            LastEmailQueried = email;
            return Task.FromResult(user);
        }
    }

    // LoginModel stores the factory, but every path exercised here fails the sign-in
    // before RecordLoginAsync runs, so the context must never be created.
    private sealed class UnusedDbContextFactory : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() =>
            throw new InvalidOperationException("DbContext must not be created on a failed sign-in.");

        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("DbContext must not be created on a failed sign-in.");
    }

    private static PasswordHasherService Hasher() => new(new PasswordHasher<AdminUser>());

    private static AdminUser ActiveUser(string password) => new()
    {
        Id = Guid.NewGuid(),
        Email = "admin@vendor.test",
        PasswordHash = Hasher().Hash(password),
        Role = AdminRole.SupportStaff,
        IsActive = true,
    };

    private static LoginModel NewModel(AdminUser? lookupResult)
    {
        var credentials = new AdminCredentialService(new FakeAdminUserLookup(lookupResult), Hasher());
        var httpContext = new DefaultHttpContext();
        return new LoginModel(credentials, new UnusedDbContextFactory(), NullLogger<LoginModel>.Instance)
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext,
                RouteData = new RouteData(),
            },
            // UrlHelper.IsLocalUrl is a pure syntactic check; no routing/HttpContext state needed.
            Url = new UrlHelper(new ActionContext(httpContext, new RouteData(), new ActionDescriptor())),
        };
    }

    // ---- returnUrl / open-redirect (criterion 4) -------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("  /ok")]                 // leading whitespace -> not a local path
    [InlineData("//evil.com")]
    [InlineData("//evil.com/path")]
    [InlineData("/\\evil.com")]           // "/\evil.com"
    [InlineData("\\/evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com")]
    [InlineData("HTTPS://EVIL.COM")]
    [InlineData("ftp://evil.com")]
    [InlineData("http:\\\\evil.com")]     // "http:\\evil.com"
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("mailto:a@b.c")]
    [InlineData("%2f%2fevil.com")]        // encoded "//": still not a local path pre-decode
    [InlineData("%5cevil.com")]
    [InlineData("\thttps://evil.com")]    // tab prefix
    [InlineData(" https://evil.com")]
    [InlineData("/\r\n/evil.com")]        // control chars inside an otherwise-local path
    [InlineData("/\t/evil.com")]          // tab at index 1: passes raw Url.IsLocalUrl, browser reads it as "//evil.com"
    [InlineData("/\nSet-Cookie: x=y")]
    [InlineData("user@evil.com")]
    [InlineData("null")]
    [InlineData("undefined")]
    public void OnGet_NonLocalReturnUrl_FallsBackToDashboard(string? returnUrl)
    {
        var model = NewModel(lookupResult: null);

        model.OnGet(returnUrl);

        Assert.Equal("/dashboard", model.ReturnUrl);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/dashboard", "/dashboard")]
    [InlineData("/pending-review", "/pending-review")]
    [InlineData("/a/b?x=1", "/a/b?x=1")]
    [InlineData("/Account/AccessDenied", "/Account/AccessDenied")]
    public void OnGet_LocalReturnUrl_IsPreserved(string returnUrl, string expected)
    {
        var model = NewModel(lookupResult: null);

        model.OnGet(returnUrl);

        Assert.Equal(expected, model.ReturnUrl);
    }

    // Characterization: Url.IsLocalUrl also accepts application-relative "~/..." as local,
    // and LoginModel returns it verbatim (LocalRedirect later expands it). Pinned so a
    // future tightening of the reducer is a conscious change.
    [Fact]
    public void OnGet_TildeSlashReturnUrl_IsTreatedAsLocal()
    {
        var model = NewModel(lookupResult: null);

        model.OnGet("~/dashboard");

        Assert.Equal("~/dashboard", model.ReturnUrl);
    }

    [Fact]
    public void OnGet_NoArgument_DefaultsToDashboard()
    {
        var model = NewModel(lookupResult: null);

        model.OnGet();

        Assert.Equal("/dashboard", model.ReturnUrl);
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http:\\\\evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("  /ok")]
    public async Task OnPostAsync_NonLocalReturnUrl_IsSanitizedEvenOnFailedSignIn(string returnUrl)
    {
        var model = NewModel(lookupResult: null);
        model.Input = new LoginModel.InputModel { Email = "nobody@vendor.test", Password = "whatever" };

        var result = await model.OnPostAsync(returnUrl);

        Assert.IsType<PageResult>(result);
        Assert.Equal("/dashboard", model.ReturnUrl);
    }

    [Fact]
    public async Task OnPostAsync_LocalReturnUrl_IsKeptOnReturnUrlProperty_EvenOnFailedSignIn()
    {
        var model = NewModel(lookupResult: null);
        model.Input = new LoginModel.InputModel { Email = "nobody@vendor.test", Password = "whatever" };

        await model.OnPostAsync("/pending-review");

        Assert.Equal("/pending-review", model.ReturnUrl);
    }

    // ---- single generic error message (criterion 4) --------------------------

    [Fact]
    public async Task OnPostAsync_UnknownEmail_RendersGenericError_AsPageResult()
    {
        var model = NewModel(lookupResult: null);
        model.Input = new LoginModel.InputModel { Email = "nobody@vendor.test", Password = "whatever" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(LoginModel.GenericError, model.Error);
    }

    [Fact]
    public async Task OnPostAsync_WrongPassword_RendersGenericError_AsPageResult()
    {
        var model = NewModel(lookupResult: ActiveUser("correct-horse-battery"));
        model.Input = new LoginModel.InputModel { Email = "admin@vendor.test", Password = "wrong" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(LoginModel.GenericError, model.Error);
    }

    [Fact]
    public async Task OnPostAsync_DisabledAccountCorrectPassword_RendersGenericError_AsPageResult()
    {
        var user = ActiveUser("right");
        user.IsActive = false;
        var model = NewModel(lookupResult: user);
        model.Input = new LoginModel.InputModel { Email = user.Email, Password = "right" };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(LoginModel.GenericError, model.Error);
    }

    [Fact]
    public async Task OnPostAsync_InvalidModelState_RendersGenericError_WithoutConsultingCredentials()
    {
        // Lookup WOULD authenticate; proving ModelState.IsValid short-circuits first.
        var model = NewModel(lookupResult: ActiveUser("right"));
        model.Input = new LoginModel.InputModel { Email = "", Password = "" };
        model.ModelState.AddModelError("Input.Email", "The Email field is required.");

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Equal(LoginModel.GenericError, model.Error);
    }

    [Fact]
    public async Task OnPostAsync_EveryFailureCause_ProducesTheIdenticalErrorString()
    {
        var unknownEmail = NewModel(lookupResult: null);
        unknownEmail.Input = new LoginModel.InputModel { Email = "nobody@vendor.test", Password = "x" };
        await unknownEmail.OnPostAsync();

        var wrongPassword = NewModel(lookupResult: ActiveUser("right"));
        wrongPassword.Input = new LoginModel.InputModel { Email = "admin@vendor.test", Password = "wrong" };
        await wrongPassword.OnPostAsync();

        var disabled = ActiveUser("right");
        disabled.IsActive = false;
        var disabledAccount = NewModel(lookupResult: disabled);
        disabledAccount.Input = new LoginModel.InputModel { Email = disabled.Email, Password = "right" };
        await disabledAccount.OnPostAsync();

        Assert.Equal(unknownEmail.Error, wrongPassword.Error);
        Assert.Equal(wrongPassword.Error, disabledAccount.Error);
        Assert.Equal(LoginModel.GenericError, unknownEmail.Error);

        // The message must not name which check failed.
        foreach (var causeWord in new[]
                 {
                     "existe", "encontr", "desactiv", "deshabilit", "bloque",
                     "incorrect", "correcta", "inactiv", "disabled", "not found",
                 })
        {
            Assert.DoesNotContain(causeWord, unknownEmail.Error!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void GenericError_IsAStableNonEmptyConstant()
    {
        Assert.False(string.IsNullOrWhiteSpace(LoginModel.GenericError));
        Assert.Equal("Correo o contraseña no válidos.", LoginModel.GenericError);
    }

    [Fact]
    public void Error_IsNull_BeforeAnySignInAttempt()
    {
        var model = NewModel(lookupResult: null);

        Assert.Null(model.Error);
        Assert.Equal("/dashboard", model.ReturnUrl);
    }
}
