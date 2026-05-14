using EnergyAi.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EnergyAi.Server.Data;

/// <summary>
/// EF Core context against the existing energy / opex schema.
/// Tables are read-only from this app's perspective — we never write counters.
/// </summary>
public class EnergyDbContext : DbContext
{
    public EnergyDbContext(DbContextOptions<EnergyDbContext> options) : base(options) { }

    public DbSet<CounterValue> CounterValues => Set<CounterValue>();
    public DbSet<TransferCode> TransferCodes => Set<TransferCode>();
    public DbSet<TransferClassCode> TransferClassCodes => Set<TransferClassCode>();
    public DbSet<Measurement> Measurements => Set<Measurement>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CounterValue>(e =>
        {
            e.ToTable("counter_value", schema: "opex");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.EquipmentCode).HasColumnName("equipment_code");
            e.Property(x => x.UtcMeasurementStartTime).HasColumnName("utc_measurement_start_time");
            e.Property(x => x.UtcMeasurementEndTime).HasColumnName("utc_measurement_end_time");
            // Quantity is modelled as double — maps to SQL Server `float`. If the DB column
            // is `decimal(p,s)` the SQL Server provider will still read it, but precision
            // may be lost for very large scales. Change to `decimal` on both sides if exact.
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.TransferCodeId).HasColumnName("transfer_code_id");
            e.Property(x => x.TransferClassCodeId).HasColumnName("transfer_class_code_id");

            e.HasOne(x => x.TransferCode)
                .WithMany()
                .HasForeignKey(x => x.TransferCodeId);

            e.HasOne(x => x.TransferClassCode)
                .WithMany()
                .HasForeignKey(x => x.TransferClassCodeId);

            // Helpful indexes for common filters — declared, not created (DB owns DDL).
            e.HasIndex(x => x.UtcMeasurementStartTime);
            e.HasIndex(x => new { x.EquipmentCode, x.UtcMeasurementStartTime });
        });

        modelBuilder.Entity<TransferCode>(e =>
        {
            e.ToTable("transfer_code", schema: "cdr");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");

            // The original SQL joins utl.measurement on m.code = tc.code (string join, not FK).
            e.HasOne(x => x.Measurement)
                .WithMany()
                .HasPrincipalKey(m => m.Code)
                .HasForeignKey(x => x.Code)
                .IsRequired(false);
        });

        modelBuilder.Entity<TransferClassCode>(e =>
        {
            e.ToTable("transfer_class_code", schema: "cdr");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
        });

        modelBuilder.Entity<Measurement>(e =>
        {
            e.ToTable("measurement", schema: "utl");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.UnitOfMeasureId).HasColumnName("unit_of_measure_id");
            e.HasAlternateKey(x => x.Code);

            e.HasOne(x => x.UnitOfMeasure)
                .WithMany()
                .HasForeignKey(x => x.UnitOfMeasureId);
        });

        modelBuilder.Entity<UnitOfMeasure>(e =>
        {
            e.ToTable("unit_of_measure", schema: "utl");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Code).HasColumnName("code");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Symbol).HasColumnName("symbol");
        });
    }
}
