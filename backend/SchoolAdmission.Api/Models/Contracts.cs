using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.Models;

public record CreateClassRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("totalSeats")] int TotalSeats
);

public record ClassResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("totalSeats")] int TotalSeats,
    [property: JsonPropertyName("heldSeats")] int HeldSeats,
    [property: JsonPropertyName("confirmedSeats")] int ConfirmedSeats,
    [property: JsonPropertyName("availableSeats")] int AvailableSeats
);

public record CreateClassResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("totalSeats")] int TotalSeats
);

public record HoldResponse(
    [property: JsonPropertyName("holdId")] string HoldId,
    [property: JsonPropertyName("classId")] string ClassId,
    [property: JsonPropertyName("studentId")] string StudentId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt
);

public record HoldStatusResponse(
    [property: JsonPropertyName("holdId")] string HoldId,
    [property: JsonPropertyName("classId")] string ClassId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt
);

public record ConfirmHoldResponse(
    [property: JsonPropertyName("holdId")] string HoldId,
    [property: JsonPropertyName("status")] string Status
);

public record MyHoldResponse(
    [property: JsonPropertyName("holdId")] string HoldId,
    [property: JsonPropertyName("classId")] string ClassId,
    [property: JsonPropertyName("className")] string ClassName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt
);

public record ErrorResponse(
    [property: JsonPropertyName("message")] string Message
);

