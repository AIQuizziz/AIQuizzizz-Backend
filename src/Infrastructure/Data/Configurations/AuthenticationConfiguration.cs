using CleanArchitectureBase.Domain.Entities;
using CleanArchitectureBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchitectureBase.Infrastructure.Data.Configurations;



public class AccountConfig : IEntityTypeConfiguration<UserAccount>
{

    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.Property(t => t.IsDeleted).HasDefaultValue(false);
        builder.HasQueryFilter(x => !x.IsDeleted);  
    }
}


public class UserConfig : IEntityTypeConfiguration<User>
{

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.FullName)
            .HasMaxLength(500);
            
        builder.HasQueryFilter(x => !x.IsDeleted && !x.IsBanned);
        
        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(u => u.IsDeleted)
            .HasDefaultValue(false);

        builder.Property(u => u.IsBanned)
            .HasDefaultValue(false);
            
        builder.Property(u => u.PaymentCode)
            .HasMaxLength(100);
            
        builder.HasIndex(u => u.PaymentCode).IsUnique();

        builder.Property(u => u.TokenCount)
            .HasDefaultValue(0);

        // ảnh đại diện: media bị xoá hẳn thì user về ảnh mặc định
        builder.HasOne(u => u.AvatarMedia)
            .WithMany()
            .HasForeignKey(u => u.AvatarMediaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne<UserAccount>()
            .WithOne(account => account.User)
            .HasForeignKey<User>(u => u.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}



public class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(x => x.UserAccountId).HasMaxLength(36);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Token).HasMaxLength(500);
        
        builder.HasQueryFilter(rt => rt.UserAccount != null && !rt.UserAccount.IsDeleted);
        
        builder.HasOne(rt => rt.UserAccount)
        .WithMany()
        .HasForeignKey(rt => rt.UserAccountId);
    }
}

