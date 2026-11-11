using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;
namespace SchoolAdmission.Api.Data;
public sealed class AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : DbContext(options)
{
    public DbSet<AdmissionClass> Classes => Set<AdmissionClass>();
    public DbSet<AdmissionHold> Holds => Set<AdmissionHold>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AdmissionClass>().HasKey(x => x.Id);
        model.Entity<AdmissionClass>().Property(x => x.Name).HasMaxLength(160).IsRequired();
        model.Entity<AdmissionClass>().ToTable(t => t.HasCheckConstraint("ck_class_total_seats_positive", "\"TotalSeats\" > 0"));
        model.Entity<AdmissionHold>().HasKey(x => x.Id);
        model.Entity<AdmissionHold>().Property(x => x.StudentId).HasMaxLength(32).IsRequired();
        model.Entity<AdmissionHold>().Property(x => x.Status).HasMaxLength(16).IsRequired();
        model.Entity<AdmissionHold>().HasOne(x => x.Class).WithMany(x => x.Holds).HasForeignKey(x => x.ClassId);
        model.Entity<AdmissionHold>().HasIndex(x => new { x.ClassId, x.StudentId, x.Status });
       
        model.Entity<AdmissionHold>()
            .HasIndex(x => x.StudentId)
            .IsUnique()
            .HasDatabaseName("ux_holds_student_one_active_application")
            .HasFilter("\"Status\" IN ('HELD', 'CONFIRMED')");
    }
}
