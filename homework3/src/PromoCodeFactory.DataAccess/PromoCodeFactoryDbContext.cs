using Microsoft.EntityFrameworkCore;
using PromoCodeFactory.Core.Domain.Administration;
using PromoCodeFactory.Core.Domain.PromoCodeManagement;

namespace PromoCodeFactory.DataAccess;

public class PromoCodeFactoryDbContext : DbContext
{
    private const int NameMaxLength = 50;
    private const int EmailMaxLength = 256;
    private const int TitleMaxLength = 100;
    private const int DescriptionMaxLength = 500;
    private const int CodeMaxLength = 100;
    private const int InfoMaxLength = 256;

    public PromoCodeFactoryDbContext(DbContextOptions<PromoCodeFactoryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<CustomerPromoCode> CustomerPromoCodes => Set<CustomerPromoCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(TitleMaxLength).IsRequired();
            b.Property(r => r.Description).HasMaxLength(DescriptionMaxLength);
        });

        modelBuilder.Entity<Employee>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.FirstName).HasMaxLength(NameMaxLength).IsRequired();
            b.Property(e => e.LastName).HasMaxLength(NameMaxLength).IsRequired();
            b.Property(e => e.Email).HasMaxLength(EmailMaxLength).IsRequired();
            b.Ignore(e => e.FullName);

            b.HasOne(e => e.Role)
                .WithMany()
                .HasForeignKey("RoleId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Preference>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(TitleMaxLength).IsRequired();
        });

        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.FirstName).HasMaxLength(NameMaxLength).IsRequired();
            b.Property(c => c.LastName).HasMaxLength(NameMaxLength).IsRequired();
            b.Property(c => c.Email).HasMaxLength(EmailMaxLength).IsRequired();
            b.Ignore(c => c.FullName);

            b.HasMany(c => c.Preferences)
                .WithMany(p => p.Customers)
                .UsingEntity("CustomerPreferences");

            b.HasMany(c => c.CustomerPromoCodes)
                .WithOne()
                .HasForeignKey(cp => cp.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PromoCode>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).HasMaxLength(CodeMaxLength).IsRequired();
            b.Property(p => p.ServiceInfo).HasMaxLength(InfoMaxLength).IsRequired();
            b.Property(p => p.PartnerName).HasMaxLength(InfoMaxLength).IsRequired();
            b.HasIndex(p => p.Code).IsUnique();

            b.HasOne(p => p.PartnerManager)
                .WithMany()
                .HasForeignKey("PartnerManagerId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(p => p.Preference)
                .WithMany()
                .HasForeignKey("PreferenceId")
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(p => p.CustomerPromoCodes)
                .WithOne()
                .HasForeignKey(cp => cp.PromoCodeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerPromoCode>(b =>
        {
            b.HasKey(cp => cp.Id);
            b.HasIndex(cp => new { cp.CustomerId, cp.PromoCodeId }).IsUnique();
        });

        base.OnModelCreating(modelBuilder);
    }
}
