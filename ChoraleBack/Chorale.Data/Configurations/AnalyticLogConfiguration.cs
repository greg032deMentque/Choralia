using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class AnalyticLogConfiguration : IEntityTypeConfiguration<AnalyticLog>
{
    public void Configure(EntityTypeBuilder<AnalyticLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Method).IsRequired().HasMaxLength(10);
        builder.Property(a => a.Path).IsRequired().HasMaxLength(500);
        builder.Property(a => a.QueryString).HasMaxLength(2000);
        builder.Property(a => a.StatusCode).IsRequired();
        builder.Property(a => a.DurationMs).IsRequired();
        builder.Property(a => a.UserId).HasMaxLength(450);
        builder.Property(a => a.IpAddressHash).HasMaxLength(64);
        builder.Property(a => a.UserAgent).HasMaxLength(512);
        builder.Property(a => a.TraceId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.Endpoint).HasMaxLength(500);
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.OccurredAt, a.StatusCode });
        builder.HasIndex(a => a.UserId).HasFilter("UserId IS NOT NULL");
    }
}
