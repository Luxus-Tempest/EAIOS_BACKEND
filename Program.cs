
using EAIOS.Api.Infrastructure;
using EAIOS.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers(options => options.Filters.Add<RequireTenantFilter>());
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<InMemoryEaiosStore>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<CurrentTenant>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<BearerTokenMiddleware>();
app.UseMiddleware<TenantResolutionMiddleware>();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapGet("/health/ready", () => Results.Ok(new { status = "Healthy", checks = new { api = "Healthy" } })).AllowAnonymous();
app.MapControllers();

app.Run();

public partial class Program;
