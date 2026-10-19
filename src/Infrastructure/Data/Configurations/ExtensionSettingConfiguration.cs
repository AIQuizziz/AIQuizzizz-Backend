using CleanArchitectureBase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureBase.Infrastructure.Data.Configurations;

public class ExtensionSettingConfiguration : IEntityTypeConfiguration<ExtensionSetting>
{
    public void Configure(EntityTypeBuilder<ExtensionSetting> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.Property(x => x.TargetMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.SourceLang).IsRequired().HasMaxLength(16);
        builder.Property(x => x.TargetLang).IsRequired().HasMaxLength(16);
        builder.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
