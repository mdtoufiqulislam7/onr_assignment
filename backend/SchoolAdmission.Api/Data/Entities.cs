using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolAdmission.Api.Data;

[Table("classes")]
public class ClassEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = string.Empty;

    [Column("name")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column("total_seats")]
    public int TotalSeats { get; set; }

    [Column("confirmed_seats")]
    public int ConfirmedSeats { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<SeatHoldEntity> Holds { get; set; } = new List<SeatHoldEntity>();
}

[Table("seat_holds")]
public class SeatHoldEntity
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = string.Empty;

    [Column("class_id")]
    [MaxLength(36)]
    public string ClassId { get; set; } = string.Empty;

    [ForeignKey(nameof(ClassId))]
    public ClassEntity? Class { get; set; }

    [Column("student_id")]
    [MaxLength(32)]
    public string StudentId { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "HELD"; // HELD, CONFIRMED, EXPIRED, CANCELLED

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

