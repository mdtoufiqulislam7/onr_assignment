using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;
using SchoolAdmission.Api.Services;
using Xunit;

namespace SchoolAdmission.Tests;

public class AdmissionIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdmissionIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing db context
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AdmissionDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // In-memory test database for deterministic fast tests
                var dbName = Guid.NewGuid().ToString();
                services.AddDbContext<AdmissionDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });

                // Mock Redis for unit/integration testing
                var redisDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IRedisCacheService));
                if (redisDescriptor != null)
                {
                    services.Remove(redisDescriptor);
                }
                services.AddSingleton<IRedisCacheService, InMemoryMockRedisService>();
            });
        });
    }

    // Test 1: Class creation and calculation of available seats
    [Fact]
    public async Task CreateClass_And_GetClasses_ReturnsCorrectCounts()
    {
        var client = _factory.CreateClient();

        var createReq = new CreateClassRequest("Class 6 - Test Shift", 10);
        var postRes = await client.PostAsJsonAsync("/classes", createReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var created = await postRes.Content.ReadFromJsonAsync<CreateClassResponse>();
        Assert.NotNull(created);
        Assert.Equal("Class 6 - Test Shift", created.Name);
        Assert.Equal(10, created.TotalSeats);

        var getRes = await client.GetAsync("/classes");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var classes = await getRes.Content.ReadFromJsonAsync<List<ClassResponse>>();
        Assert.NotNull(classes);
        var target = classes.FirstOrDefault(c => c.Id == created.Id);
        Assert.NotNull(target);
        Assert.Equal(10, target.AvailableSeats);
        Assert.Equal(0, target.HeldSeats);
    }

    // Test 2: Only own active hold can be confirmed; idempotency check
    [Fact]
    public async Task ConfirmHold_OnlyOwnHoldAndIdempotent()
    {
        var client = _factory.CreateClient();

        // 1. Create Class
        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Class 7 - Test", 5));
        var createdClass = await classRes.Content.ReadFromJsonAsync<CreateClassResponse>();

        // 2. Student A holds seat
        var studentA = "20152691234567891";
        client.DefaultRequestHeaders.Remove("X-Student-Id");
        client.DefaultRequestHeaders.Add("X-Student-Id", studentA);

        var holdRes = await client.PostAsync($"/classes/{createdClass!.Id}/holds", null);
        Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);
        var hold = await holdRes.Content.ReadFromJsonAsync<HoldResponse>();

        // 3. Student B attempts to confirm Student A's hold -> 403 Forbidden
        var studentB = "20152691234567892";
        client.DefaultRequestHeaders.Remove("X-Student-Id");
        client.DefaultRequestHeaders.Add("X-Student-Id", studentB);

        var unauthorizedConfirm = await client.PostAsync($"/holds/{hold!.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedConfirm.StatusCode);

        // 4. Student A confirms their own hold -> 200 OK
        client.DefaultRequestHeaders.Remove("X-Student-Id");
        client.DefaultRequestHeaders.Add("X-Student-Id", studentA);

        var confirmRes = await client.PostAsync($"/holds/{hold.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);

        // 5. Duplicate confirm call -> Idempotent, returns 200 OK without incrementing twice
        var duplicateConfirm = await client.PostAsync($"/holds/{hold.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, duplicateConfirm.StatusCode);
    }

    // Test 3: Concurrency Test - 50 simultaneous hold requests for 5 seats
    // Asserts that held + confirmed seats NEVER exceed totalSeats
    [Fact]
    public async Task ConcurrencyTest_SeatsNeverExceedTotalSeats()
    {
        var client = _factory.CreateClient();

        const int totalSeats = 5;
        const int concurrentStudents = 30;

        // 1. Create class with only 5 seats
        var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Class 10 - Highly Competitive", totalSeats));
        var createdClass = await classRes.Content.ReadFromJsonAsync<CreateClassResponse>();
        Assert.NotNull(createdClass);

        // 2. Fire 30 concurrent hold requests simultaneously
        var tasks = Enumerable.Range(1, concurrentStudents).Select(async i =>
        {
            var reqClient = _factory.CreateClient();
            var studentId = $"201526912345678{i:D2}";
            reqClient.DefaultRequestHeaders.Add("X-Student-Id", studentId);

            var res = await reqClient.PostAsync($"/classes/{createdClass.Id}/holds", null);
            return res.StatusCode;
        });

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(s => s == HttpStatusCode.Created);
        var conflictCount = results.Count(s => s == HttpStatusCode.Conflict);

        // Crucial validation: success must not exceed total available seats!
        Assert.True(successCount <= totalSeats, $"Success count ({successCount}) exceeded totalSeats ({totalSeats})!");
        Assert.Equal(totalSeats, successCount);
        Assert.Equal(concurrentStudents - totalSeats, conflictCount);

        // Verify class state
        var classStateRes = await client.GetAsync($"/classes/{createdClass.Id}");
        var classState = await classStateRes.Content.ReadFromJsonAsync<ClassResponse>();
        Assert.NotNull(classState);
        Assert.Equal(0, classState.AvailableSeats);
        Assert.Equal(totalSeats, classState.HeldSeats + classState.ConfirmedSeats);
    }

    // Test 4: Auto-Expiration releases held seat automatically without manual cancel
    [Fact]
    public async Task AutoExpire_ReleasesSeatAutomaticallyAfterDuration()
    {
        Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "2");
        try
        {
            var client = _factory.CreateClient();

            // 1. Create class with 1 seat
            var classRes = await client.PostAsJsonAsync("/classes", new CreateClassRequest("Class 8 - AutoExpire Shift", 1));
            var createdClass = await classRes.Content.ReadFromJsonAsync<CreateClassResponse>();
            Assert.NotNull(createdClass);

            // 2. Student 1 holds the only seat
            client.DefaultRequestHeaders.Add("X-Student-Id", "20152691234567801");
            var holdRes = await client.PostAsync($"/classes/{createdClass.Id}/holds", null);
            Assert.Equal(HttpStatusCode.Created, holdRes.StatusCode);
            var hold = await holdRes.Content.ReadFromJsonAsync<HoldResponse>();
            Assert.NotNull(hold);

            // Seat is now held (0 available)
            var stateBefore = await client.GetFromJsonAsync<ClassResponse>($"/classes/{createdClass.Id}");
            Assert.Equal(1, stateBefore!.HeldSeats);
            Assert.Equal(0, stateBefore.AvailableSeats);

            // 3. Wait 2.5 seconds for hold to expire automatically without calling cancel
            await Task.Delay(2500);

            // 4. Verify hold status is now EXPIRED and seat returned to availableSeats (1 available)
            var holdStatus = await client.GetFromJsonAsync<HoldStatusResponse>($"/holds/{hold.HoldId}");
            Assert.Equal("EXPIRED", holdStatus!.Status);

            var stateAfter = await client.GetFromJsonAsync<ClassResponse>($"/classes/{createdClass.Id}");
            Assert.Equal(0, stateAfter!.HeldSeats);
            Assert.Equal(1, stateAfter.AvailableSeats);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "600");
        }
    }
}

public class InMemoryMockRedisService : IRedisCacheService
{
    private readonly Dictionary<string, object> _cache = new();
    private readonly object _lock = new();

    public Task<T?> GetAsync<T>(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var val) && val is T typed)
            {
                return Task.FromResult<T?>(typed);
            }
            return Task.FromResult<T?>(default);
        }
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        lock (_lock)
        {
            if (value != null) _cache[key] = value;
            return Task.CompletedTask;
        }
    }

    public Task RemoveByPrefixAsync(params string[] prefixes)
    {
        lock (_lock)
        {
            var keysToRemove = _cache.Keys.Where(k => prefixes.Any(k.StartsWith)).ToList();
            foreach (var k in keysToRemove) _cache.Remove(k);
            return Task.CompletedTask;
        }
    }

    public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        lock (_lock)
        {
            if (!_cache.ContainsKey(key))
            {
                _cache[key] = value;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task<bool> ReleaseLockAsync(string key, string value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var current) && current?.ToString() == value)
            {
                _cache.Remove(key);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task EnqueuePayloadAsync<T>(string queueKey, T payload) => Task.CompletedTask;
}

