using Microsoft.EntityFrameworkCore;

namespace SchoolAdmission.Api.Data;

public class AdmissionDbContext : DbContext
{
    public AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : base(options)
    {
    }

    public DbSet<ClassEntity> Classes => Set<ClassEntity>();
    public DbSet<SeatHoldEntity> SeatHolds => Set<SeatHoldEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ClassEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.TotalSeats).IsRequired();
            entity.Property(e => e.ConfirmedSeats).HasDefaultValue(0);
        });

        modelBuilder.Entity<SeatHoldEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.StudentId).IsRequired().HasMaxLength(32);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ExpiresAt).IsRequired();

            // PostgreSQL unique partial index:
            // A student cannot have more than one active (HELD or CONFIRMED) record in the same class at the same time!
            entity.HasIndex(e => new { e.ClassId, e.StudentId })
                  .HasFilter("status IN ('HELD', 'CONFIRMED')")
                  .IsUnique();

            entity.HasIndex(e => new { e.ClassId, e.Status, e.ExpiresAt });

            entity.HasOne(e => e.Class)
                  .WithMany(c => c.Holds)
                  .HasForeignKey(e => e.ClassId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

