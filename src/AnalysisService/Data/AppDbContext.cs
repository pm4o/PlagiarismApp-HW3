using AnalysisService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AnalysisService.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Work> Works => Set<Work>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<SubmissionIdempotency> Submissions => Set<SubmissionIdempotency>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Work>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.StudentId).IsRequired();
            e.Property(x => x.StudentName).IsRequired();
            e.Property(x => x.AssignmentId).IsRequired();
            e.Property(x => x.SubmittedAtUtc).IsRequired();
            e.Property(x => x.Status).IsRequired();

            e.HasIndex(x => new { x.AssignmentId, x.FileHashSha256 });
        });

        mb.Entity<Report>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.ResultJson).IsRequired();

            e.HasOne(x => x.Work).WithMany().HasForeignKey(x => x.WorkId);

            e.HasIndex(x => x.WorkId);
            e.HasIndex(x => new { x.WorkId, x.Type }).IsUnique();
        });

        mb.Entity<SubmissionIdempotency>(e =>
        {
            e.HasKey(x => x.IdempotencyKey);
            e.Property(x => x.RequestHash).IsRequired();
            e.Property(x => x.Status).IsRequired();
            e.Property(x => x.CreatedAtUtc).IsRequired();
            e.Property(x => x.UpdatedAtUtc).IsRequired();
            e.HasIndex(x => x.WorkId);
        });
    }
}