using EAIOS.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EAIOS.Api.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", "identity");
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.HasIndex(u => u.NormalizedEmail).IsUnique();
        b.Property(u => u.Email).HasMaxLength(320).IsRequired();
        b.Property(u => u.NormalizedEmail).HasMaxLength(320).IsRequired();
        b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        b.Property(u => u.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(u => u.Locale).HasMaxLength(10).HasDefaultValue("fr");
        b.Property(u => u.TimeZone).HasMaxLength(60).HasDefaultValue("UTC");
        b.Property(u => u.PasswordHash).HasMaxLength(500);
        b.Property(u => u.PasswordResetToken).HasMaxLength(200);
        b.Property(u => u.EmailVerificationToken).HasMaxLength(200);
        b.Property(u => u.AvatarUrl).HasMaxLength(2048);
        b.Property(u => u.JobTitle).HasMaxLength(200);
        b.Property(u => u.Department).HasMaxLength(200);
        b.Property(u => u.SuspensionReason).HasMaxLength(500);
        b.HasMany(u => u.Sessions).WithOne().HasForeignKey(s => s.UserId);
        b.HasMany(u => u.ApiKeys).WithOne().HasForeignKey(a => a.UserId);
        b.HasMany(u => u.MfaCredentials).WithOne().HasForeignKey(m => m.UserId);
    }
}

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions", "identity");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.HasIndex(s => s.RefreshTokenHash).IsUnique();
        b.HasIndex(s => new { s.UserId, s.Status });
        b.Property(s => s.RefreshTokenHash).HasMaxLength(500).IsRequired();
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(s => s.DeviceFingerprint).HasMaxLength(200);
        b.Property(s => s.IpAddress).HasMaxLength(45);
        b.Property(s => s.RevocationReason).HasMaxLength(200);
    }
}

public sealed class MfaCredentialConfiguration : IEntityTypeConfiguration<MfaCredential>
{
    public void Configure(EntityTypeBuilder<MfaCredential> b)
    {
        b.ToTable("mfa_credentials", "identity");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.HasIndex(m => new { m.UserId, m.Method });
        b.Property(m => m.Method).HasConversion<string>().HasMaxLength(30);
        b.Property(m => m.SecretEncrypted).HasMaxLength(1000);
    }
}

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("api_keys", "identity");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.HasIndex(a => a.KeyHash).IsUnique();
        b.HasIndex(a => new { a.UserId, a.IsActive });
        b.Property(a => a.Name).HasMaxLength(200).IsRequired();
        b.Property(a => a.KeyPrefix).HasMaxLength(30).IsRequired();
        b.Property(a => a.KeyHash).HasMaxLength(200).IsRequired();
    }
}

public sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("invitations", "identity");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.HasIndex(i => i.Token).IsUnique();
        b.HasIndex(i => new { i.NormalizedEmail, i.Status });
        b.Property(i => i.Email).HasMaxLength(320).IsRequired();
        b.Property(i => i.NormalizedEmail).HasMaxLength(320).IsRequired();
        b.Property(i => i.Token).HasMaxLength(500).IsRequired();
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(30);
    }
}
