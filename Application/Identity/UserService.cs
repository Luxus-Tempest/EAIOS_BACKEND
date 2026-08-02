using EAIOS.Api.Domain.Identity;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Identity;
using EAIOS.Api.Infrastructure.Security;

namespace EAIOS.Api.Application.Identity;

public interface IUserService
{
    Task<User> UpdateProfileAsync(Guid userId, string firstName, string lastName, string? jobTitle, string? department, string? locale, string? timeZone, CancellationToken ct = default);
    Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default);
}

public sealed class UserService(
    IUserRepository userRepo,
    IPasswordService passwordService) : IUserService
{
    public async Task<User> UpdateProfileAsync(
        Guid userId, string firstName, string lastName, 
        string? jobTitle, string? department, 
        string? locale, string? timeZone, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct) 
                   ?? throw new InvalidOperationException("Utilisateur introuvable.");

        user.UpdateProfile(firstName, lastName, jobTitle, department, locale, timeZone);
        userRepo.Update(user);
        await userRepo.SaveAsync(ct);

        return user;
    }

    public async Task ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await userRepo.GetByIdAsync(userId, ct)
                   ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (!passwordService.VerifyPassword(currentPassword, user.PasswordHash ?? ""))
            throw new ArgumentException("Mot de passe actuel incorrect.");

        if (!passwordService.IsStrongPassword(newPassword))
            throw new ArgumentException("Le nouveau mot de passe est trop faible.");

        user.SetPasswordHash(passwordService.HashPassword(newPassword));
        userRepo.Update(user);
        await userRepo.SaveAsync(ct);
    }
}
