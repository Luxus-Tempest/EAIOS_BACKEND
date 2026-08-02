using EAIOS.Api.Infrastructure;
using EAIOS.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using FluentValidation;
using FluentValidation.AspNetCore;
using System.Security.Cryptography;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ────────────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddConsole(opts => opts.FormatterName = "simple");
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

// ── Infrastructure (DbContexts, Repositories, Services) ──────────────────
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddHttpContextAccessor();

// ── Validation ────────────────────────────────────────────────────────────
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<EAIOS.Api.Application.Common.Validators.LoginRequestValidator>();

// ── Authentication JWT ────────────────────────────────────────────────────
var tokenSecret = builder.Configuration["Security:TokenSigningKey"] ?? "eaios-dev-signing-key-CHANGE-IN-PRODUCTION-must-be-at-least-64-characters-long!";
var tokenKey    = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(tokenSecret)) { KeyId = "eaios-key" };

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = tokenKey,
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// ── API Controllers ───────────────────────────────────────────────────────
builder.Services.AddControllers(options => 
    {
        options.Filters.Add<EAIOS.Api.Middleware.ValidationFilter>();
    })
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
    
// Disable automatic 400 response to let our ValidationFilter handle it
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

// ── OpenAPI ───────────────────────────────────────────────────────────────
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "EAIOS API";
        document.Info.Version = "v1.0";

        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization",
            Description = "Saisissez votre token JWT"
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = scheme;

        var req = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(req);
        return Task.CompletedTask;
    });
});

// ── Exception Handler ─────────────────────────────────────────────────────
builder.Services.AddExceptionHandler<EAIOS.Api.Middleware.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ── CORS ──────────────────────────────────────────────────────────────────
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p =>
        p.WithOrigins(builder.Configuration["Cors:AllowedOrigins"]?.Split(',') ?? ["http://localhost:3000", "http://localhost:5173"])
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

// ── Health Checks ─────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();
app.UseMiddleware<EAIOS.Api.Middleware.CorrelationIdMiddleware>();
app.UseMiddleware<EAIOS.Api.Middleware.RateLimitingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<EAIOS.Api.Middleware.TenantResolutionMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

// ── Seed données de développement ────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    await SeedDevelopmentDataAsync(app);
}

await app.RunAsync();

// ═════════════════════════════════════════════════════════════════════════════
// Helper : seed données initiales pour l'environnement de développement
// ═════════════════════════════════════════════════════════════════════════════

static async Task SeedDevelopmentDataAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var sp     = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILogger<Program>>();

    try
    {
        var eaiosDb    = sp.GetRequiredService<EAIOS.Api.Infrastructure.Persistence.EaiosDbContext>();
        var platformDb = sp.GetRequiredService<EAIOS.Api.Infrastructure.Persistence.PlatformDbContext>();
        var pwdService = sp.GetRequiredService<EAIOS.Api.Infrastructure.Security.IPasswordService>();

        // Organisation de démo
        var orgId   = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var adminId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        var org = new EAIOS.Api.Domain.Organization.Organization
        {
            Id     = orgId,
            Name   = "EAIOS Demo",
            Slug   = "eaios-demo",
            Status = EAIOS.Api.Domain.Organization.OrganizationStatus.Active
        };
        if (!platformDb.Organizations.Any())
        {
            await platformDb.Organizations.AddAsync(org);
            await platformDb.SaveChangesAsync();
        }

        // Résoudre tenant pour le seeding
        var tenantCtx = sp.GetRequiredService<EAIOS.Api.Application.Common.Interfaces.ITenantContext>();
        tenantCtx.SetTenant(orgId);

        // Utilisateur admin
        var existingAdmin = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(eaiosDb.Users, u => u.NormalizedEmail == "ADMIN@EAIOS.IO");
        if (existingAdmin == null)
        {
            var admin = EAIOS.Api.Domain.Identity.User.Create(orgId, "admin@eaios.io", "Admin", "EAIOS");
            admin.SetPasswordHash(pwdService.HashPassword("Admin@123456!"));
            admin.Activate();

            await eaiosDb.Users.AddAsync(admin);
            await eaiosDb.SaveChangesAsync();

            logger.LogInformation("✅ Seeded admin user: admin@eaios.io / Admin@123456!");
        }
        else
        {
            existingAdmin.SetPasswordHash(pwdService.HashPassword("Admin@123456!"));
            existingAdmin.Activate();
            eaiosDb.Users.Update(existingAdmin);
            await eaiosDb.SaveChangesAsync();
            logger.LogInformation("✅ Updated admin user status to Active: admin@eaios.io / Admin@123456!");
        }

        logger.LogInformation("✅ Development seed complete for org {OrgId}", orgId);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "❌ Seed failed: {Message}", ex.Message);
    }
}
