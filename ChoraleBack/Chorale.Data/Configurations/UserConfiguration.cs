using ChoraleBackEnd.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChoraleBackEnd.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Firstname).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Lastname).IsRequired().HasMaxLength(100);
        builder.HasQueryFilter(u => !u.IsDeleted);
    }
}
