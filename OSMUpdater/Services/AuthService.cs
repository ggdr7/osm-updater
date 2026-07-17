using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OsmUpdateUtility.Data;
using OsmUpdateUtility.Models;

namespace OsmUpdateUtility.Services;

public interface IAuthService
{
    Task<bool> AuthenticateAsync(string username, string password);
    Task SignInAsync(HttpContext context, string username);
    Task SignOutAsync(HttpContext context);
    Task<bool> UserExistsAsync();
    Task<bool> UsernameExistsAsync(string username);
    Task CreateUserAsync(string username, string password);
    Task<bool> ChangePasswordAsync(string username, string oldPassword, string newPassword);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;

    public AuthService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<bool> AuthenticateAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return false;

        return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
    }

    public async Task SignInAsync(HttpContext context, string username)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, username)
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
        };

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity),
            authProperties);
    }

    public async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task<bool> UserExistsAsync()
    {
        return await _db.Users.AnyAsync();
    }
    public async Task<bool> UsernameExistsAsync(string username)
    {
        return await _db.Users.AnyAsync(u => u.Username == username);
    }

    public async Task CreateUserAsync(string username, string password)
    {
        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null) return false;

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync();
        return true;
    }
}