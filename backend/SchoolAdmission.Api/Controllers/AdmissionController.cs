using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;
using SchoolAdmission.Api.Services;

namespace SchoolAdmission.Api.Controllers;

[ApiController]
[Route("")]
public class AdmissionController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ClassLocks = new();
    private readonly AdmissionDbContext _dbContext;
    private readonly IRedisCacheService _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<AdmissionController> _logger;

    public AdmissionController(
        AdmissionDbContext dbContext,
        IRedisCacheService redis,
        IConfiguration config,
        ILogger<AdmissionController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _config = config;
        _logger = logger;
    }

    private int GetHoldDurationSeconds()
    {
        var envVal = Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS");
        if (!string.IsNullOrWhiteSpace(envVal) && int.TryParse(envVal, out var secs) && secs > 0)
        {
            return secs;
        }

        var configVal = _config.GetValue<int?>("HOLD_DURATION_SECONDS");
        return (configVal.HasValue && configVal.Value > 0) ? configVal.Value : 600;
    }

    private string? GetStudentId(out string? validationError)
    {
        validationError = null;
        if (Request.Headers.TryGetValue("X-Student-Id", out var values))
        {
            var id = values.FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(id))
            {
                if (id.Length > 32)
                {
                    validationError = "X-Student-Id cannot exceed 32 characters.";
                    return null;
                }
                return id;
            }
        }
        validationError = "Missing or empty X-Student-Id header.";
        return null;
    }

    // 1. POST /classes
    [HttpPost("classes")]
    public async Task<IActionResult> CreateClass([FromBody] CreateClassRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ErrorResponse("Class name is required."));
        }

        if (request.TotalSeats <= 0)
        {
            return BadRequest(new ErrorResponse("Total seats must be greater than zero."));
        }

        var classEntity = new ClassEntity
        {
            Id = $"c_{Guid.NewGuid().ToString("N")[..8]}",
            Name = request.Name.Trim(),
            TotalSeats = request.TotalSeats,
            ConfirmedSeats = 0,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Classes.Add(classEntity);
        await _dbContext.SaveChangesAsync();

        // Invalidate Redis cache
        await _redis.RemoveByPrefixAsync("classes:list");

        var response = new CreateClassResponse(classEntity.Id, classEntity.Name, classEntity.TotalSeats);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    // 2. GET /classes
    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses([FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var cacheKey = $"classes:list:p{page}:s{pageSize}";
        var cached = await _redis.GetAsync<List<ClassResponse>>(cacheKey);
        if (cached != null)
        {
            return Ok(cached);
        }

        var now = DateTime.UtcNow;

        var query = _dbContext.Classes
            .OrderBy(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TotalSeats,
                c.ConfirmedSeats,
                HeldSeats = c.Holds.Count(h => h.Status == "HELD" && h.ExpiresAt > now)
            });

        var totalItems = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalItems.ToString();

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }

        var results = await query.ToListAsync();

        var responseList = results.Select(r =>
        {
            var available = r.TotalSeats - (r.ConfirmedSeats + r.HeldSeats);
            if (available < 0) available = 0;
            return new ClassResponse(
                r.Id,
                r.Name,
                r.TotalSeats,
                r.HeldSeats,
                r.ConfirmedSeats,
                available
            );
        }).ToList();

        await _redis.SetAsync(cacheKey, responseList, TimeSpan.FromSeconds(2));
        return Ok(responseList);
    }

    // 3. GET /classes/{classId}
    [HttpGet("classes/{classId}")]
    public async Task<IActionResult> GetClassById(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
        {
            return BadRequest(new ErrorResponse("Class ID is required."));
        }

        var cacheKey = $"class:{classId}";
        var cached = await _redis.GetAsync<ClassResponse>(cacheKey);
        if (cached != null)
        {
            return Ok(cached);
        }

        var now = DateTime.UtcNow;
        var classInfo = await _dbContext.Classes
            .Where(c => c.Id == classId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TotalSeats,
                c.ConfirmedSeats,
                HeldSeats = c.Holds.Count(h => h.Status == "HELD" && h.ExpiresAt > now)
            })
            .FirstOrDefaultAsync();

        if (classInfo == null)
        {
            return NotFound(new ErrorResponse("Class not found."));
        }

        var available = classInfo.TotalSeats - (classInfo.ConfirmedSeats + classInfo.HeldSeats);
        if (available < 0) available = 0;

        var response = new ClassResponse(
            classInfo.Id,
            classInfo.Name,
            classInfo.TotalSeats,
            classInfo.HeldSeats,
            classInfo.ConfirmedSeats,
            available
        );

        await _redis.SetAsync(cacheKey, response, TimeSpan.FromSeconds(2));
        return Ok(response);
    }

    // 4. POST /classes/{classId}/holds
    [HttpPost("classes/{classId}/holds")]
    public async Task<IActionResult> CreateHold(string classId)
    {
        var studentId = GetStudentId(out var studentErr);
        if (studentId == null)
        {
            return BadRequest(new ErrorResponse(studentErr!));
        }

        if (string.IsNullOrWhiteSpace(classId))
        {
            return BadRequest(new ErrorResponse("Invalid class ID."));
        }

        // Buffer payload in Redis queue for audit/high throughput ingestion
        await _redis.EnqueuePayloadAsync($"hold_queue:{classId}", new
        {
            ClassId = classId,
            StudentId = studentId,
            EnqueuedAt = DateTime.UtcNow
        });

        var holdDuration = GetHoldDurationSeconds();
        var lockKey = $"lock:class:{classId}";
        var lockValue = Guid.NewGuid().ToString();

        var classSemaphore = ClassLocks.GetOrAdd(classId, _ => new SemaphoreSlim(1, 1));
        await classSemaphore.WaitAsync();

        // Distributed lock (Redis) combined with PostgreSQL Row Lock for bonus multi-process safety
        var acquired = await _redis.AcquireLockAsync(lockKey, lockValue, TimeSpan.FromSeconds(5));

        var isRelational = _dbContext.Database.IsRelational();
        using var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
            : null;
        try
        {
            var now = DateTime.UtcNow;

            // Row-level lock on the class (PostgreSQL FOR UPDATE)
            // Ensures even if multiple processes run simultaneously on different ports,
            // only one process can check and increment seats at a time!
            ClassEntity? targetClass;
            if (isRelational)
            {
                targetClass = await _dbContext.Classes
                    .FromSqlInterpolated($"SELECT * FROM classes WHERE id = {classId} FOR UPDATE")
                    .FirstOrDefaultAsync();
            }
            else
            {
                targetClass = await _dbContext.Classes.FirstOrDefaultAsync(c => c.Id == classId);
            }

            if (targetClass == null)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return NotFound(new ErrorResponse("Class not found."));
            }

            // Expire any outdated HELD records immediately within this transaction
            var staleHolds = await _dbContext.SeatHolds
                .Where(h => h.ClassId == classId && h.Status == "HELD" && h.ExpiresAt <= now)
                .ToListAsync();

            if (staleHolds.Count > 0)
            {
                foreach (var sh in staleHolds)
                {
                    sh.Status = "EXPIRED";
                    sh.UpdatedAt = now;
                }
                await _dbContext.SaveChangesAsync();
            }

            // Rule 3: Check if this student already has an active hold or confirmed admission in this class
            var existingActiveHold = await _dbContext.SeatHolds
                .AnyAsync(h => h.ClassId == classId &&
                               h.StudentId == studentId &&
                               (h.Status == "CONFIRMED" || (h.Status == "HELD" && h.ExpiresAt > now)));

            if (existingActiveHold)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Conflict(new ErrorResponse("Student already has an active hold or confirmed admission in this class."));
            }

            // Calculate active seats
            var activeHeldCount = await _dbContext.SeatHolds
                .CountAsync(h => h.ClassId == classId && h.Status == "HELD" && h.ExpiresAt > now);

            var totalOccupied = targetClass.ConfirmedSeats + activeHeldCount;

            // Rule 1: Under no circumstance held + confirmed can exceed totalSeats
            if (totalOccupied >= targetClass.TotalSeats)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return Conflict(new ErrorResponse("No available seats left in this class."));
            }

            // Create new hold
            var holdEntity = new SeatHoldEntity
            {
                Id = $"h_{Guid.NewGuid().ToString("N")[..8]}",
                ClassId = classId,
                StudentId = studentId,
                Status = "HELD",
                ExpiresAt = now.AddSeconds(holdDuration),
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.SeatHolds.Add(holdEntity);
            await _dbContext.SaveChangesAsync();

            if (transaction != null) await transaction.CommitAsync();

            // Invalidate caches
            await _redis.RemoveByPrefixAsync("classes:list", $"class:{classId}", $"student:{studentId}");

            var response = new HoldResponse(
                holdEntity.Id,
                holdEntity.ClassId,
                holdEntity.StudentId,
                holdEntity.Status,
                holdEntity.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            );

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (Exception ex)
        {
            if (transaction != null) await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to create hold for class {ClassId} student {StudentId}", classId, studentId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("An error occurred while creating hold."));
        }
        finally
        {
            if (acquired)
            {
                await _redis.ReleaseLockAsync(lockKey, lockValue);
            }
            classSemaphore.Release();
        }
    }

    // 5. GET /holds/{holdId}
    [HttpGet("holds/{holdId}")]
    public async Task<IActionResult> GetHold(string holdId)
    {
        if (string.IsNullOrWhiteSpace(holdId))
        {
            return BadRequest(new ErrorResponse("Hold ID is required."));
        }

        var hold = await _dbContext.SeatHolds.FirstOrDefaultAsync(h => h.Id == holdId);
        if (hold == null)
        {
            return NotFound(new ErrorResponse("Hold not found."));
        }

        // Live status calculation: if HELD and expired, treat as EXPIRED
        var status = hold.Status;
        if (status == "HELD" && hold.ExpiresAt <= DateTime.UtcNow)
        {
            status = "EXPIRED";
        }

        var response = new HoldStatusResponse(
            hold.Id,
            hold.ClassId,
            status,
            hold.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        );

        return Ok(response);
    }

    // 6. POST /holds/{holdId}/confirm
    [HttpPost("holds/{holdId}/confirm")]
    public async Task<IActionResult> ConfirmHold(string holdId)
    {
        var studentId = GetStudentId(out var studentErr);
        if (studentId == null)
        {
            return BadRequest(new ErrorResponse(studentErr!));
        }

        if (string.IsNullOrWhiteSpace(holdId))
        {
            return BadRequest(new ErrorResponse("Hold ID is required."));
        }

        var isRelational = _dbContext.Database.IsRelational();
        using var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
            : null;
        try
        {
            var hold = await _dbContext.SeatHolds
                .Include(h => h.Class)
                .FirstOrDefaultAsync(h => h.Id == holdId);

            if (hold == null)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return NotFound(new ErrorResponse("Hold not found."));
            }

            // Rule 4: Only student's own hold can be confirmed
            if (hold.StudentId != studentId)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("You can only confirm your own hold."));
            }

            // Rule 5: Idempotency - if already confirmed, confirmedSeats only increases once
            if (hold.Status == "CONFIRMED")
            {
                if (transaction != null) await transaction.CommitAsync();
                return Ok(new ConfirmHoldResponse(hold.Id, "CONFIRMED"));
            }

            // Rule 4: Cannot confirm if expired or cancelled
            if (hold.Status != "HELD" || hold.ExpiresAt <= DateTime.UtcNow)
            {
                if (hold.Status == "HELD")
                {
                    hold.Status = "EXPIRED";
                    hold.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                }
                if (transaction != null) await transaction.CommitAsync();
                return BadRequest(new ErrorResponse("Hold has expired or is invalid. Cannot confirm."));
            }

            // Transition to CONFIRMED and increment class confirmed seats
            hold.Status = "CONFIRMED";
            hold.UpdatedAt = DateTime.UtcNow;

            if (hold.Class != null)
            {
                hold.Class.ConfirmedSeats += 1;
            }

            await _dbContext.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            await _redis.RemoveByPrefixAsync("classes:list", $"class:{hold.ClassId}", $"student:{studentId}");

            return Ok(new ConfirmHoldResponse(hold.Id, "CONFIRMED"));
        }
        catch (Exception ex)
        {
            if (transaction != null) await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to confirm hold {HoldId}", holdId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("An error occurred during confirmation."));
        }
    }

    // 7. DELETE /holds/{holdId}
    [HttpDelete("holds/{holdId}")]
    public async Task<IActionResult> CancelHold(string holdId)
    {
        var studentId = GetStudentId(out var studentErr);
        if (studentId == null)
        {
            return BadRequest(new ErrorResponse(studentErr!));
        }

        if (string.IsNullOrWhiteSpace(holdId))
        {
            return BadRequest(new ErrorResponse("Hold ID is required."));
        }

        var isRelational = _dbContext.Database.IsRelational();
        using var transaction = isRelational
            ? await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted)
            : null;
        try
        {
            var hold = await _dbContext.SeatHolds
                .Include(h => h.Class)
                .FirstOrDefaultAsync(h => h.Id == holdId);

            if (hold == null)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return NotFound(new ErrorResponse("Hold not found."));
            }

            if (hold.StudentId != studentId)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse("You can only cancel your own hold."));
            }

            // If it was CONFIRMED, decrement confirmed seats
            if (hold.Status == "CONFIRMED" && hold.Class != null && hold.Class.ConfirmedSeats > 0)
            {
                hold.Class.ConfirmedSeats -= 1;
            }

            hold.Status = "CANCELLED";
            hold.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();

            await _redis.RemoveByPrefixAsync("classes:list", $"class:{hold.ClassId}", $"student:{studentId}");

            return NoContent();
        }
        catch (Exception ex)
        {
            if (transaction != null) await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to cancel hold {HoldId}", holdId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse("An error occurred during cancellation."));
        }
    }

    // 8. GET /me/holds
    [HttpGet("me/holds")]
    public async Task<IActionResult> GetMyHolds([FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var studentId = GetStudentId(out var studentErr);
        if (studentId == null)
        {
            return BadRequest(new ErrorResponse(studentErr!));
        }

        var cacheKey = $"student:{studentId}:holds:p{page}:s{pageSize}";
        var cached = await _redis.GetAsync<List<MyHoldResponse>>(cacheKey);
        if (cached != null)
        {
            return Ok(cached);
        }

        var now = DateTime.UtcNow;

        var query = _dbContext.SeatHolds
            .Include(h => h.Class)
            .Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.CreatedAt);

        var totalItems = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalItems.ToString();

        List<SeatHoldEntity> holds;
        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            holds = await query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value).ToListAsync();
        }
        else
        {
            holds = await query.ToListAsync();
        }

        var responseList = holds.Select(h =>
        {
            var status = h.Status;
            if (status == "HELD" && h.ExpiresAt <= now)
            {
                status = "EXPIRED";
            }

            return new MyHoldResponse(
                h.Id,
                h.ClassId,
                h.Class?.Name ?? "Unknown Class",
                status,
                h.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
            );
        }).ToList();

        await _redis.SetAsync(cacheKey, responseList, TimeSpan.FromSeconds(2));
        return Ok(responseList);
    }
}

