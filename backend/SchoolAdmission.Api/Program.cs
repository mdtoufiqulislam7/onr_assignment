using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Controller and Swagger setup
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Database Connection from Environment Variable or AppSettings
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=postgres;Port=5432;Database=admission_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AdmissionDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

// 3. Redis Connection
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_URL")
    ?? builder.Configuration.GetConnectionString("Redis")
    ?? "redis:6379";

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    try
    {
        var configuration = ConfigurationOptions.Parse(redisConnectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = 3000;
        return ConnectionMultiplexer.Connect(configuration);
    }
    catch (Exception ex)
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Failed to connect to Redis initially. Running with fallback.");
        return ConnectionMultiplexer.Connect("localhost:6379,abortConnect=false");
    }
});

builder.Services.AddSingleton<IRedisCacheService, RedisCacheService>();

// 4. Background Expiration Worker (Runs continuously to auto-expire holds)
builder.Services.AddHostedService<HoldExpirationWorker>();

// 5. Rate Limiter for POST requests (Fixed window per student or IP)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("PostRateLimit", httpContext =>
    {
        // Rate limit POST endpoints based on X-Student-Id or Client IP
        string partitionKey = "anonymous";
        if (httpContext.Request.Headers.TryGetValue("X-Student-Id", out var studentId) && !string.IsNullOrWhiteSpace(studentId))
        {
            partitionKey = $"student_{studentId}";
        }
        else
        {
            partitionKey = $"ip_{httpContext.Connection.RemoteIpAddress}";
        }

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, // generous burst limit for concurrency tests while protecting server
            Window = TimeSpan.FromSeconds(10),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 200
        });
    });
});

// 6. CORS setup so React UI can call API from any host/port
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Total-Count");
    });
});

var app = builder.Build();

// Auto-run DB Migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Ensuring PostgreSQL database schema is created...");
        db.Database.EnsureCreated();
        logger.LogInformation("Database ready.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Could not ensure database was created at startup.");
    }
}

app.UseCors("AllowAll");
app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers().RequireRateLimiting("PostRateLimit");

app.Run();

// For WebApplicationFactory in integration tests
public partial class Program { }

