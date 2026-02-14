using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PCTimeLimitServer.Api;
using PCTimeLimitServer.Configuration;
using PCTimeLimitServer.Domain.Entities;
using PCTimeLimitServer.Infrastructure;
using PCTimeLimitServer.Infrastructure.Security;
using PCTimeLimitShared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var securityOptions = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
if (builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(securityOptions.JwtSigningKey) || securityOptions.JwtSigningKey.Contains("CHANGE_ME", StringComparison.Ordinal))
    {
        securityOptions.JwtSigningKey = "DEV_ONLY_JWT_SIGNING_KEY_FOR_LOCAL_TESTS";
    }
    if (string.IsNullOrWhiteSpace(securityOptions.OpsKey) || securityOptions.OpsKey.Contains("CHANGE_ME", StringComparison.Ordinal))
    {
        securityOptions.OpsKey = "DEV_ONLY_OPS_KEY";
    }
}
else
{
    if (string.IsNullOrWhiteSpace(securityOptions.JwtSigningKey) || securityOptions.JwtSigningKey.Contains("CHANGE_ME", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Security:JwtSigningKey must be configured with a secure random value.");
    }
    if (string.IsNullOrWhiteSpace(securityOptions.OpsKey) || securityOptions.OpsKey.Contains("CHANGE_ME", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Security:OpsKey must be configured with a secure random value.");
    }
}

builder.Services.AddSingleton(securityOptions);
builder.Services.AddSingleton<JwtTokenFactory>();
builder.Services.AddScoped<IPasswordHasher<AdminUser>, PasswordHasher<AdminUser>>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=pctimelimit.db";

builder.Services.AddDbContext<PCTimeLimitDbContext>(options => options.UseSqlite(connectionString));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = securityOptions.JwtIssuer,
            ValidAudience = securityOptions.JwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(securityOptions.JwtSigningKey))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("pairing", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PCTimeLimitDbContext>();
    await db.Database.MigrateAsync();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var backfilled = await AllowedUsageScheduleService.BackfillLegacySchedulesAsync(db, startupLogger, CancellationToken.None);
    if (backfilled > 0)
    {
        startupLogger.LogInformation("Backfilled {BackfilledCount} legacy allowed-usage schedules into normalized rows.", backfilled);
    }
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live");
app.MapGet("/health/ready", async (PCTimeLimitDbContext dbContext, CancellationToken cancellationToken) =>
{
    return await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/", () => Results.Ok(new { service = "PCTimeLimitServer", status = "ok" }));

var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");

auth.MapPost("/register-admin", async (
    RegisterAdminRequest request,
    PCTimeLimitDbContext dbContext,
    IPasswordHasher<AdminUser> passwordHasher,
    JwtTokenFactory jwtFactory,
    CancellationToken cancellationToken) =>
{
    var username = request.Username?.Trim() ?? string.Empty;
    var password = request.Password ?? string.Empty;

    if (username.Length < 3 || username.Length > 50)
    {
        return Results.BadRequest(new { message = "Username must be between 3 and 50 characters." });
    }

    if (password.Length < 6 || password.Length > 100)
    {
        return Results.BadRequest(new { message = "Password must be between 6 and 100 characters." });
    }

    var normalizedUsername = username.ToUpperInvariant();
    var exists = await dbContext.AdminUsers.AnyAsync(x => x.NormalizedUsername == normalizedUsername, cancellationToken);
    if (exists)
    {
        return Results.Conflict(new { message = "Account already exists." });
    }

    var adminCode = await GenerateUniqueAdminCodeAsync(dbContext, cancellationToken);

    var user = new AdminUser
    {
        Id = Guid.NewGuid(),
        Username = username,
        NormalizedUsername = normalizedUsername,
        AdminCode = adminCode,
        CreatedAtUtc = DateTime.UtcNow,
        LastLoginAtUtc = DateTime.UtcNow
    };
    user.PasswordHash = passwordHasher.HashPassword(user, password);

    dbContext.AdminUsers.Add(user);

    var now = DateTime.UtcNow;
    var refreshTokenValue = TokenUtility.GenerateToken();
    dbContext.RefreshTokens.Add(new RefreshToken
    {
        Id = Guid.NewGuid(),
        AdminUserId = user.Id,
        TokenHash = TokenUtility.HashToken(refreshTokenValue),
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddDays(securityOptions.RefreshTokenLifetimeDays)
    });

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new TokenResponse
    {
        Success = true,
        Message = "Admin account created successfully.",
        Username = user.Username,
        AdminCode = user.AdminCode,
        AccessToken = jwtFactory.CreateAccessToken(user),
        RefreshToken = refreshTokenValue,
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(securityOptions.AccessTokenLifetimeMinutes)
    });
});

auth.MapPost("/login", async (
    LoginRequest request,
    PCTimeLimitDbContext dbContext,
    IPasswordHasher<AdminUser> passwordHasher,
    JwtTokenFactory jwtFactory,
    CancellationToken cancellationToken) =>
{
    var normalizedUsername = (request.Username ?? string.Empty).Trim().ToUpperInvariant();
    var password = request.Password ?? string.Empty;

    var user = await dbContext.AdminUsers.SingleOrDefaultAsync(x => x.NormalizedUsername == normalizedUsername, cancellationToken);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var verifyResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
    if (verifyResult == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    user.LastLoginAtUtc = DateTime.UtcNow;

    var now = DateTime.UtcNow;
    var refreshTokenValue = TokenUtility.GenerateToken();
    dbContext.RefreshTokens.Add(new RefreshToken
    {
        Id = Guid.NewGuid(),
        AdminUserId = user.Id,
        TokenHash = TokenUtility.HashToken(refreshTokenValue),
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddDays(securityOptions.RefreshTokenLifetimeDays)
    });

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new TokenResponse
    {
        Success = true,
        Message = "Login successful.",
        Username = user.Username,
        AdminCode = user.AdminCode,
        AccessToken = jwtFactory.CreateAccessToken(user),
        RefreshToken = refreshTokenValue,
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(securityOptions.AccessTokenLifetimeMinutes)
    });
});

auth.MapPost("/refresh", async (
    RefreshTokenRequest request,
    PCTimeLimitDbContext dbContext,
    JwtTokenFactory jwtFactory,
    CancellationToken cancellationToken) =>
{
    var rawRefreshToken = request.RefreshToken?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(rawRefreshToken))
    {
        return Results.Unauthorized();
    }

    var now = DateTime.UtcNow;
    var refreshTokenHash = TokenUtility.HashToken(rawRefreshToken);

    var existing = await dbContext.RefreshTokens
        .Include(x => x.AdminUser)
        .SingleOrDefaultAsync(x => x.TokenHash == refreshTokenHash, cancellationToken);

    if (existing is null || existing.RevokedAtUtc is not null || existing.ExpiresAtUtc <= now)
    {
        return Results.Unauthorized();
    }

    var newRefreshTokenValue = TokenUtility.GenerateToken();
    var newRefreshTokenHash = TokenUtility.HashToken(newRefreshTokenValue);

    existing.RevokedAtUtc = now;
    existing.ReplacedByTokenHash = newRefreshTokenHash;

    dbContext.RefreshTokens.Add(new RefreshToken
    {
        Id = Guid.NewGuid(),
        AdminUserId = existing.AdminUserId,
        TokenHash = newRefreshTokenHash,
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddDays(securityOptions.RefreshTokenLifetimeDays)
    });

    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new TokenResponse
    {
        Success = true,
        Message = "Token refreshed.",
        Username = existing.AdminUser.Username,
        AdminCode = existing.AdminUser.AdminCode,
        AccessToken = jwtFactory.CreateAccessToken(existing.AdminUser),
        RefreshToken = newRefreshTokenValue,
        AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(securityOptions.AccessTokenLifetimeMinutes)
    });
});

auth.MapPost("/logout", async (
    LogoutRequest request,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var rawRefreshToken = request.RefreshToken?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(rawRefreshToken))
    {
        return Results.Ok();
    }

    var refreshTokenHash = TokenUtility.HashToken(rawRefreshToken);
    var existing = await dbContext.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == refreshTokenHash, cancellationToken);

    if (existing is not null && existing.RevokedAtUtc is null)
    {
        existing.RevokedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok();
});

var child = app.MapGroup("/api/v1/child").RequireRateLimiting("pairing");

child.MapPost("/register", async (
    RegisterChildRequest request,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var adminCode = (request.AdminCode ?? string.Empty).Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(adminCode))
    {
        return Results.BadRequest(new { message = "Admin code is required." });
    }

    var computerId = (request.ComputerId ?? string.Empty).Trim();
    var computerName = (request.ComputerName ?? string.Empty).Trim();

    if (string.IsNullOrWhiteSpace(computerId) || string.IsNullOrWhiteSpace(computerName))
    {
        return Results.BadRequest(new { message = "Computer ID and name are required." });
    }

    var admin = await dbContext.AdminUsers.SingleOrDefaultAsync(x => x.AdminCode == adminCode, cancellationToken);
    if (admin is null)
    {
        return Results.Unauthorized();
    }

    var now = DateTime.UtcNow;

    var computer = await dbContext.Computers
        .Include(x => x.DeviceCredential)
        .Include(x => x.AllowedUsageRanges)
        .SingleOrDefaultAsync(x => x.ExternalId == computerId, cancellationToken);

    if (computer is null)
    {
        computer = new Computer
        {
            Id = Guid.NewGuid(),
            ExternalId = computerId,
            ComputerName = computerName,
            AdminUserId = admin.Id,
            RegisteredAtUtc = now,
            LastSeenUtc = now,
            IsOnline = true,
            DailyTimeLimitSeconds = (int)TimeSpan.FromHours(1).TotalSeconds,
            AllowedUsageUpdatedAtUtc = now,
            AllowedUsageJson = string.Empty
        };
        dbContext.Computers.Add(computer);
    }
    else
    {
        computer.ComputerName = computerName;
        computer.AdminUserId = admin.Id;
        computer.LastSeenUtc = now;
        computer.IsOnline = true;
    }

    if (computer.DeviceCredential is not null)
    {
        computer.DeviceCredential.RevokedAtUtc = now;
    }

    var deviceToken = TokenUtility.GenerateToken();
    var deviceCredential = new DeviceCredential
    {
        Id = Guid.NewGuid(),
        ComputerId = computer.Id,
        TokenHash = TokenUtility.HashToken(deviceToken),
        CreatedAtUtc = now,
        ExpiresAtUtc = now.AddDays(securityOptions.DeviceTokenLifetimeDays),
        LastSeenUtc = now,
        RevokedAtUtc = null
    };

    dbContext.DeviceCredentials.Add(deviceCredential);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new RegisterChildResponse
    {
        Success = true,
        Message = "Computer registered successfully.",
        DeviceToken = deviceToken,
        DailyLimit = TimeSpan.FromSeconds(computer.DailyTimeLimitSeconds),
        AllowedUsageSchedule = AllowedUsageScheduleService.GetScheduleForComputer(computer)
    });
});

child.MapPost("/status", async (
    UpdateStatusRequest request,
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var authResult = await DeviceAuthenticator.AuthenticateAsync(httpContext, dbContext, cancellationToken);
    if (!authResult.IsAuthenticated || authResult.Computer is null || authResult.DeviceCredential is null)
    {
        return Results.Unauthorized();
    }

    var now = DateTime.UtcNow;
    authResult.Computer.IsOnline = request.IsOnline;
    authResult.Computer.LastSeenUtc = now;
    authResult.DeviceCredential.LastSeenUtc = now;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new QueueActionResponse { Success = true, Message = "Status updated." });
});

child.MapGet("/state", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var authResult = await DeviceAuthenticator.AuthenticateAsync(httpContext, dbContext, cancellationToken);
    if (!authResult.IsAuthenticated || authResult.Computer is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new ComputerStateResponse
    {
        Success = true,
        Message = "State loaded.",
        DailyLimit = TimeSpan.FromSeconds(authResult.Computer.DailyTimeLimitSeconds),
        PendingReset = authResult.Computer.PendingReset,
        PendingForceLockout = authResult.Computer.PendingForceLockout,
        AllowedUsageSchedule = AllowedUsageScheduleService.GetScheduleForComputer(authResult.Computer)
    });
});

child.MapPost("/ack-reset", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var authResult = await DeviceAuthenticator.AuthenticateAsync(httpContext, dbContext, cancellationToken);
    if (!authResult.IsAuthenticated || authResult.Computer is null)
    {
        return Results.Unauthorized();
    }

    authResult.Computer.PendingReset = false;
    authResult.Computer.LastSeenUtc = DateTime.UtcNow;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new QueueActionResponse { Success = true, Message = "Reset acknowledged." });
});

child.MapPost("/ack-force-lockout", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var authResult = await DeviceAuthenticator.AuthenticateAsync(httpContext, dbContext, cancellationToken);
    if (!authResult.IsAuthenticated || authResult.Computer is null)
    {
        return Results.Unauthorized();
    }

    authResult.Computer.PendingForceLockout = false;
    authResult.Computer.LastSeenUtc = DateTime.UtcNow;

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new QueueActionResponse { Success = true, Message = "Force lockout acknowledged." });
});

var adminGroup = app.MapGroup("/api/v1/admin").RequireAuthorization();

adminGroup.MapGet("/computers", async (
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computers = await dbContext.Computers
        .Include(x => x.AdminUser)
        .Include(x => x.AllowedUsageRanges)
        .Where(x => x.AdminUserId == adminUserId.Value)
        .OrderBy(x => x.ComputerName)
        .ToListAsync(cancellationToken);

    return Results.Ok(new ComputersResponse
    {
        Success = true,
        Message = "Computers loaded.",
        Computers = computers.Select(x => x.ToDto()).ToList()
    });
});

adminGroup.MapPut("/computers/{computerId}/time-limit", async (
    string computerId,
    SetTimeLimitRequest request,
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (request.DailyTimeLimit < TimeSpan.Zero || request.DailyTimeLimit > TimeSpan.FromHours(24))
    {
        return Results.BadRequest(new { message = "Daily time limit must be between 00:00:00 and 24:00:00." });
    }

    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computer = await dbContext.Computers.SingleOrDefaultAsync(
        x => x.ExternalId == computerId && x.AdminUserId == adminUserId.Value,
        cancellationToken);

    if (computer is null)
    {
        return Results.NotFound();
    }

    computer.DailyTimeLimitSeconds = (int)request.DailyTimeLimit.TotalSeconds;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new QueueActionResponse { Success = true, Message = "Time limit updated." });
});

adminGroup.MapPut("/computers/{computerId}/allowed-usage", async (
    string computerId,
    SetAllowedUsageRequest request,
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computer = await dbContext.Computers
        .Include(x => x.AllowedUsageRanges)
        .SingleOrDefaultAsync(
        x => x.ExternalId == computerId && x.AdminUserId == adminUserId.Value,
        cancellationToken);

    if (computer is null)
    {
        return Results.NotFound();
    }

    var (canonicalSchedule, errors) = AllowedUsageScheduleService.ValidateAndCanonicalize(request.Ranges);
    if (canonicalSchedule is null)
    {
        logger.LogWarning(
            "Allowed usage update rejected. AdminUserId: {AdminUserId}, ComputerId: {ComputerId}, Errors: {Errors}",
            adminUserId.Value,
            computerId,
            string.Join("; ", errors));

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["ranges"] = errors.ToArray()
        });
    }

    var currentSchedule = AllowedUsageScheduleService.GetScheduleForComputer(computer);
    if (AllowedUsageScheduleService.AreEquivalent(currentSchedule, canonicalSchedule))
    {
        return Results.Ok(new AllowedUsageScheduleResponse
        {
            Success = true,
            Message = "Allowed usage unchanged.",
            Schedule = currentSchedule
        });
    }

    canonicalSchedule.UpdatedAtUtc = DateTime.UtcNow;

    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    await AllowedUsageScheduleService.ApplyScheduleAsync(dbContext, computer, canonicalSchedule, cancellationToken);
    await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    logger.LogInformation(
        "Allowed usage updated. AdminUserId: {AdminUserId}, ComputerId: {ComputerId}, BeforeCount: {BeforeCount}, AfterCount: {AfterCount}",
        adminUserId.Value,
        computerId,
        currentSchedule.Ranges.Count,
        canonicalSchedule.Ranges.Count);

    return Results.Ok(new AllowedUsageScheduleResponse
    {
        Success = true,
        Message = "Allowed usage updated.",
        Schedule = canonicalSchedule
    });
});

adminGroup.MapGet("/computers/{computerId}/allowed-usage", async (
    string computerId,
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computer = await dbContext.Computers
        .Include(x => x.AllowedUsageRanges)
        .SingleOrDefaultAsync(
            x => x.ExternalId == computerId && x.AdminUserId == adminUserId.Value,
            cancellationToken);

    if (computer is null)
    {
        return Results.NotFound();
    }

    var schedule = AllowedUsageScheduleService.GetScheduleForComputer(computer);
    return Results.Ok(new AllowedUsageScheduleResponse
    {
        Success = true,
        Message = "Allowed usage loaded.",
        Schedule = schedule
    });
});

adminGroup.MapPost("/computers/{computerId}/reset", async (
    string computerId,
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computer = await dbContext.Computers.SingleOrDefaultAsync(
        x => x.ExternalId == computerId && x.AdminUserId == adminUserId.Value,
        cancellationToken);

    if (computer is null)
    {
        return Results.NotFound();
    }

    computer.PendingReset = true;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new QueueActionResponse { Success = true, Message = "Reset queued." });
});

adminGroup.MapPost("/computers/{computerId}/force-lockout", async (
    string computerId,
    ClaimsPrincipal user,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var adminUserId = GetAdminUserId(user);
    if (adminUserId is null)
    {
        return Results.Unauthorized();
    }

    var computer = await dbContext.Computers.SingleOrDefaultAsync(
        x => x.ExternalId == computerId && x.AdminUserId == adminUserId.Value,
        cancellationToken);

    if (computer is null)
    {
        return Results.NotFound();
    }

    computer.PendingForceLockout = true;
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new QueueActionResponse { Success = true, Message = "Force lockout queued." });
});

var ops = app.MapGroup("/api/v1/ops");

ops.MapGet("/status", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    var adminCount = await dbContext.AdminUsers.CountAsync(cancellationToken);
    var computerCount = await dbContext.Computers.CountAsync(cancellationToken);

    return Results.Ok(new OpsStatusResponse
    {
        AdminCount = adminCount,
        ComputerCount = computerCount,
        ServerTimeUtc = DateTime.UtcNow
    });
});

ops.MapPost("/create-admin", async (
    HttpContext httpContext,
    OpsCreateAdminRequest request,
    PCTimeLimitDbContext dbContext,
    IPasswordHasher<AdminUser> passwordHasher,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    var username = request.Username?.Trim() ?? string.Empty;
    var password = request.Password ?? string.Empty;

    if (username.Length < 3 || username.Length > 50 || password.Length < 6 || password.Length > 100)
    {
        return Results.BadRequest(new { message = "Invalid username/password length." });
    }

    var normalizedUsername = username.ToUpperInvariant();
    if (await dbContext.AdminUsers.AnyAsync(x => x.NormalizedUsername == normalizedUsername, cancellationToken))
    {
        return Results.Conflict(new { message = "Account already exists." });
    }

    var adminCode = await GenerateUniqueAdminCodeAsync(dbContext, cancellationToken);

    var user = new AdminUser
    {
        Id = Guid.NewGuid(),
        Username = username,
        NormalizedUsername = normalizedUsername,
        AdminCode = adminCode,
        CreatedAtUtc = DateTime.UtcNow
    };
    user.PasswordHash = passwordHasher.HashPassword(user, password);

    dbContext.AdminUsers.Add(user);
    await dbContext.SaveChangesAsync(cancellationToken);

    return Results.Ok(new OpsUserDto
    {
        Username = user.Username,
        AdminCode = user.AdminCode,
        CreatedAtUtc = user.CreatedAtUtc,
        LastLoginAtUtc = user.LastLoginAtUtc
    });
});

ops.MapGet("/users", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    var users = await dbContext.AdminUsers
        .OrderBy(x => x.Username)
        .Select(x => new OpsUserDto
        {
            Username = x.Username,
            AdminCode = x.AdminCode,
            CreatedAtUtc = x.CreatedAtUtc,
            LastLoginAtUtc = x.LastLoginAtUtc
        })
        .ToListAsync(cancellationToken);

    return Results.Ok(new OpsUsersResponse { Users = users });
});

ops.MapGet("/computers", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    var computers = await dbContext.Computers
        .Include(x => x.AdminUser)
        .Include(x => x.AllowedUsageRanges)
        .OrderBy(x => x.ComputerName)
        .ToListAsync(cancellationToken);

    return Results.Ok(new OpsComputersResponse
    {
        Computers = computers.Select(x => x.ToDto()).ToList()
    });
});

ops.MapDelete("/users", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    dbContext.AdminUsers.RemoveRange(dbContext.AdminUsers);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new QueueActionResponse { Success = true, Message = "All admin users deleted." });
});

ops.MapDelete("/computers", async (
    HttpContext httpContext,
    PCTimeLimitDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!OpsAuthorization.HasValidOpsKey(httpContext.Request, securityOptions))
    {
        return Results.Unauthorized();
    }

    dbContext.Computers.RemoveRange(dbContext.Computers);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new QueueActionResponse { Success = true, Message = "All computers deleted." });
});

app.Run();

static Guid? GetAdminUserId(ClaimsPrincipal user)
{
    var value = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return Guid.TryParse(value, out var parsed) ? parsed : null;
}

static async Task<string> GenerateUniqueAdminCodeAsync(PCTimeLimitDbContext dbContext, CancellationToken cancellationToken)
{
    for (var i = 0; i < 32; i++)
    {
        var candidate = AdminCodeGenerator.Generate();
        var exists = await dbContext.AdminUsers.AnyAsync(x => x.AdminCode == candidate, cancellationToken);
        if (!exists)
        {
            return candidate;
        }
    }

    throw new InvalidOperationException("Unable to generate a unique admin code.");
}
