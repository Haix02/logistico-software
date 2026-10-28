using Logistico.Modules.Shipments.Domain;
using Logistico.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Logistico.Modules.Shipments.Infrastructure;

internal sealed class ShipmentsDbContext(DbContextOptions<ShipmentsDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public const string Schema = "shipment";
    public const string TrackingSequence = "tracking_number_seq";
    public const string TransferSequence = "transfer_number_seq";
    public const string RequestSequence = "request_number_seq";
    public const string ReturnManifestSequence = "return_manifest_seq";
    public const string SettlementSequence = "settlement_number_seq";
    public const string CashHandoverSequence = "cash_handover_seq";
    public const string ShelfCheckSequence = "shelf_check_seq";

    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentEvent> ShipmentEvents => Set<ShipmentEvent>();
    public DbSet<CustomsDeclaration> CustomsDeclarations => Set<CustomsDeclaration>();
    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<CodSettlement> CodSettlements => Set<CodSettlement>();
    public DbSet<CashHandover> CashHandovers => Set<CashHandover>();
    public DbSet<HubTransfer> Transfers => Set<HubTransfer>();
    public DbSet<TransferItem> TransferItems => Set<TransferItem>();
    public DbSet<ServiceRequest> Requests => Set<ServiceRequest>();
    public DbSet<OpsAlert> Alerts => Set<OpsAlert>();
    public DbSet<AutomationSettings> AutomationSettings => Set<AutomationSettings>();
    public DbSet<AutomationLogEntry> AutomationLog => Set<AutomationLogEntry>();
    public DbSet<CustomerNotification> Notifications => Set<CustomerNotification>();
    public DbSet<HubScan> HubScans => Set<HubScan>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<RateCardSettings> RateCards => Set<RateCardSettings>();
    public DbSet<SavedView> SavedViews => Set<SavedView>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();
    public DbSet<ShipmentComment> Comments => Set<ShipmentComment>();
    public DbSet<ShelfCheck> ShelfChecks => Set<ShelfCheck>();
    public DbSet<ShelfCheckLine> ShelfCheckLines => Set<ShelfCheckLine>();

    // Referenced by the query filters so EF Core parameterises the tenant per context instance.
    private Guid CurrentTenantId => tenant.TenantId;

    /// <summary>SQL naming a governorate code in English and Arabic (empty for other codes), for the search column.</summary>
    private static string GovernorateNamesSql(string column) =>
        $"CASE {column} "
        + string.Concat(Logistico.SharedKernel.Iraq.IraqGovernorates.All.Select(g => $"WHEN '{g.Code}' THEN '{g.NameEn.Replace("'", "''", StringComparison.Ordinal)} {g.NameAr}' "))
        + "ELSE '' END";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("pg_trgm");
        SearchSql.Register(modelBuilder);
        modelBuilder.HasSequence<long>(TrackingSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(TransferSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(RequestSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(ReturnManifestSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(SettlementSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(CashHandoverSequence).StartsAt(1).IncrementsBy(1);
        modelBuilder.HasSequence<long>(ShelfCheckSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.Entity<Shipment>(e =>
        {
            e.ToTable("shipments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.ServiceType).HasConversion<string>().HasMaxLength(16).HasDefaultValue(Contracts.ServiceType.International).HasSentinel(Contracts.ServiceType.International);
            e.Property(x => x.HouseBillNumber).HasMaxLength(40);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Incoterm).HasMaxLength(3);
            e.Property(x => x.MerchantName).HasMaxLength(150);
            e.Property(x => x.ExternalReference).HasMaxLength(60);
            e.Property(x => x.ShipperName).HasMaxLength(200);
            e.Property(x => x.ConsigneeName).HasMaxLength(200);
            e.Property(x => x.ConsigneePhone).HasMaxLength(24);
            e.Property(x => x.OriginCode).HasMaxLength(5);
            e.Property(x => x.DestinationCode).HasMaxLength(5);
            e.Property(x => x.DestinationCity).HasMaxLength(80);
            e.Property(x => x.DeliveryAddress).HasMaxLength(300);
            e.Property(x => x.Landmark).HasMaxLength(200);
            e.Property(x => x.ItemsDescription).HasMaxLength(500);
            e.Property(x => x.GrossWeightKg).HasPrecision(12, 3);
            e.Property(x => x.VolumeCbm).HasPrecision(12, 4);
            e.Property(x => x.ChargeableWeightKg).HasPrecision(12, 3);
            e.Property(x => x.DeclaredValue).HasPrecision(19, 4);
            e.Property(x => x.DeclaredCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.CodAmount).HasPrecision(19, 4);
            e.Property(x => x.CodCurrency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.DeliveryFee).HasPrecision(19, 4);
            e.Property(x => x.ReturnFee).HasPrecision(19, 4);
            e.Property(x => x.WarehouseBin).HasMaxLength(20);
            e.Property(x => x.CourierName).HasMaxLength(100);
            e.Property(x => x.LastFailureReason).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.OriginHubCode).HasMaxLength(8);
            e.Property(x => x.DestinationHubCode).HasMaxLength(8).HasDefaultValue(Contracts.Rules.IraqHubs.Baghdad);
            e.Property(x => x.Confirmation).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.ConfirmationBy).HasMaxLength(256);
            e.Property(x => x.RatingComment).HasMaxLength(500);
            e.Property(x => x.CurrentHubCode).HasMaxLength(8);
            e.Property(x => x.ReturnReason).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.ReturnManifestNumber).HasMaxLength(24);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsReturning);
            e.Ignore(x => x.TargetHubCode);
            e.Ignore(x => x.NeedsTransfer);

            e.HasIndex(x => new { x.TenantId, x.TrackingNumber }).IsUnique();
            // Public tracking and customer self-service find a parcel by its number across companies, so numbers must be
            // unique everywhere, not only within a company (they come from one shared sequence; this makes it a guarantee).
            e.HasIndex(x => x.TrackingNumber).IsUnique().HasDatabaseName("ux_shipments_tracking_number");
            // Automation sweeps and hub screens: "parcels in status X since before T", "parcels at hub H".
            e.HasIndex(x => new { x.TenantId, x.Status, x.LastStatusAt });
            e.HasIndex(x => new { x.TenantId, x.CurrentHubCode, x.Status }).HasFilter("current_hub_code IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.DestinationCode, x.Status });
            e.HasIndex(x => new { x.TenantId, x.ReturnManifestNumber }).HasFilter("return_manifest_number IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.Status, x.EtaAt });
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.MerchantId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.ConsigneePhone });
            e.HasIndex(x => new { x.TenantId, x.WarehouseBin }).HasFilter("warehouse_bin IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.CodSettlementId }).HasFilter("cod_settlement_id IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.DeliveredAt }).HasFilter("delivered_at IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.ConfirmationRequestedAt }).HasFilter("confirmation = 'Pending'");
            e.HasIndex(x => new { x.TenantId, x.RatedAt }).HasFilter("rated_at IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.ReturnRequestedAt }).HasFilter("return_requested_at IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.CodCollectedAt }).HasFilter("cod_collected_at IS NOT NULL");
            // Cash still with couriers: collected on delivery, not yet handed in at the office.
            e.HasIndex(x => new { x.TenantId, x.CourierName }).HasFilter("cod_collected_at IS NOT NULL AND cod_handed_over_at IS NULL")
                .HasDatabaseName("ix_shipments_cash_with_couriers");
            // Free-text search reads one folded column (SearchText): tracking number, customer, phone, shop, shipper, the
            // shop's order number, the area and the destination governorate in both languages. PostgreSQL keeps it
            // current; a trigram index makes "contains" fast.
            e.Property<string>(ShipmentSearch.Column)
                .HasColumnName("search_text")
                .HasComputedColumnSql(SearchText.Sql(
                    "tracking_number || ' ' || consignee_name || ' ' || coalesce(consignee_phone, '') || ' ' || coalesce(merchant_name, '') || ' ' "
                    + "|| shipper_name || ' ' || coalesce(external_reference, '') || ' ' || coalesce(destination_city, '') || ' ' || "
                    + GovernorateNamesSql("destination_code")), stored: true);
            e.HasIndex(ShipmentSearch.Column).HasMethod("gin").HasOperators("gin_trgm_ops").HasDatabaseName("ix_shipments_search_text");

            e.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field);

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ShipmentEvent>(e =>
        {
            e.ToTable("shipment_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.WarehouseBin).HasMaxLength(20);
            e.Property(x => x.CourierName).HasMaxLength(100);
            e.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.RecordedBy).HasMaxLength(256);

            e.HasIndex(x => new { x.ShipmentId, x.OccurredAt });
            e.HasIndex(x => new { x.TenantId, x.Status, x.OccurredAt }).HasDatabaseName("ix_shipment_events_tenant_status_occurred");

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CustomsDeclaration>(e =>
        {
            e.ToTable("customs_declarations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.CustomsOfficeCode).HasMaxLength(10);
            e.Property(x => x.Regime).HasMaxLength(10);
            e.Property(x => x.DeclarationNumber).HasMaxLength(40);
            e.Property(x => x.AssessmentNumber).HasMaxLength(40);
            e.Property(x => x.ReceiptNumber).HasMaxLength(40);
            e.Property(x => x.ReleaseOrderNumber).HasMaxLength(40);
            e.Property(x => x.SelectivityLane).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.HsCode).HasMaxLength(16);
            e.Property(x => x.GoodsDescription).HasMaxLength(500);
            e.Property(x => x.CountryOfOrigin).HasMaxLength(2);
            e.Property(x => x.CooNumber).HasMaxLength(60);
            e.Property(x => x.CommercialInvoiceNumber).HasMaxLength(60);
            e.Property(x => x.CocNumber).HasMaxLength(60);
            e.Property(x => x.CustomsValueUsd).HasPrecision(19, 4);
            e.Property(x => x.DutyRatePercent).HasPrecision(6, 2);
            e.Property(x => x.AssessedDutyIqd).HasPrecision(19, 4);
            e.Property(x => x.TaxAndFeesIqd).HasPrecision(19, 4);
            e.Property(x => x.HoldReason).HasMaxLength(500);
            e.Property(x => x.BrokerName).HasMaxLength(150);
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();

            e.HasIndex(x => new { x.TenantId, x.ShipmentId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.DeclarationNumber }).HasFilter("declaration_number IS NOT NULL");

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Courier>(e =>
        {
            e.ToTable("couriers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(24);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Governorate).HasMaxLength(5);
            e.Property(x => x.VehicleType).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.VehiclePlate).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();

            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsActive, x.Governorate });

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ShelfCheck>(e =>
        {
            e.ToTable("shelf_checks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.HubCode).HasMaxLength(16);
            e.Property(x => x.Bin).HasMaxLength(20);
            e.Property(x => x.StartedBy).HasMaxLength(256);
            e.Property(x => x.FinishedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ShelfCheckId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.HubCode, x.StartedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ShelfCheckLine>(e =>
        {
            e.ToTable("shelf_check_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TrackingNumber).HasMaxLength(40);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.SystemHubCode).HasMaxLength(16);
            e.Property(x => x.SystemBin).HasMaxLength(20);
            e.Property(x => x.SystemStatus).HasConversion<string>().HasMaxLength(32);

            // A parcel appears once per check, even when two people scan it at the same moment.
            e.HasIndex(x => new { x.ShelfCheckId, x.TrackingNumber }).IsUnique();
            e.HasIndex(x => new { x.ShelfCheckId, x.State });
        });

        modelBuilder.Entity<ShipmentComment>(e =>
        {
            e.ToTable("shipment_comments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Author).HasMaxLength(256);
            e.Property(x => x.AuthorName).HasMaxLength(120);
            e.Property(x => x.Text).HasMaxLength(ShipmentComment.MaxLength);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.ShipmentId, x.CreatedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<DeliveryProof>(e =>
        {
            e.ToTable("delivery_proofs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FailureReason).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ReceivedBy).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.CourierLabel).HasMaxLength(160);
            e.Property(x => x.ReceiverName).HasMaxLength(120);
            e.Property(x => x.Signature).HasMaxLength(Contracts.CourierDeliveryRequest.MaxSignatureLength);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TenantId, x.ShipmentId, x.RecordedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<SavedView>(e =>
        {
            e.ToTable("saved_views");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserId).HasMaxLength(64);
            e.Property(x => x.List).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Query).HasMaxLength(1000);

            // One name per person and list: saving under a name again replaces that view.
            e.HasIndex(x => new { x.TenantId, x.UserId, x.List, x.Name }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CashHandover>(e =>
        {
            e.ToTable("cash_handovers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.CourierLabel).HasMaxLength(160);
            e.Property(x => x.ExpectedIqd).HasPrecision(19, 4);
            e.Property(x => x.ReceivedIqd).HasPrecision(19, 4);
            e.Property(x => x.DifferenceIqd).HasPrecision(19, 4);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.ReceivedBy).HasMaxLength(256);

            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CourierId, x.ReceivedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CodSettlement>(e =>
        {
            e.ToTable("cod_settlements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SettlementNumber).HasMaxLength(32);
            e.Property(x => x.MerchantName).HasMaxLength(150);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.GrossCodAmount).HasPrecision(19, 4);
            e.Property(x => x.DeliveryFeesAmount).HasPrecision(19, 4);
            e.Property(x => x.ReturnFeesAmount).HasPrecision(19, 4);
            e.Property(x => x.NetPayoutAmount).HasPrecision(19, 4);
            e.Property(x => x.PayoutMethod).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PayoutReference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.SettledBy).HasMaxLength(256);

            e.HasIndex(x => new { x.TenantId, x.SettlementNumber }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.MerchantId, x.SettledAt });

            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<HubTransfer>(e =>
        {
            e.ToTable("transfers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(24);
            e.Property(x => x.FromHubCode).HasMaxLength(8);
            e.Property(x => x.ToHubCode).HasMaxLength(8);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.VehiclePlate).HasMaxLength(30);
            e.Property(x => x.DriverName).HasMaxLength(100);
            e.Property(x => x.SealNumber).HasMaxLength(30);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.DepartedBy).HasMaxLength(256);
            e.Property(x => x.ReceivedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();

            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.FromHubCode, x.ToHubCode });
            // One loading truck per route: two scanner stations opening a truck at the same moment cannot both win.
            e.HasIndex(x => new { x.TenantId, x.FromHubCode, x.ToHubCode })
                .IsUnique()
                .HasFilter("status = 'Loading'")
                .HasDatabaseName("ux_transfers_one_loading_per_route");

            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<TransferItem>(e =>
        {
            e.ToTable("transfer_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(x => new { x.TransferId, x.ShipmentId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ShipmentId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<ServiceRequest>(e =>
        {
            e.ToTable("service_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(24);
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Details).HasMaxLength(1000);
            e.Property(x => x.NewGovernorate).HasMaxLength(5);
            e.Property(x => x.NewCity).HasMaxLength(80);
            e.Property(x => x.NewAddress).HasMaxLength(300);
            e.Property(x => x.NewLandmark).HasMaxLength(200);
            e.Property(x => x.NewPhone).HasMaxLength(24);
            e.Property(x => x.ResolvedBy).HasMaxLength(256);
            e.Property(x => x.Resolution).HasMaxLength(500);
            e.Property(x => x.CreatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsOpen);

            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status, x.DueAt });
            e.HasIndex(x => new { x.TenantId, x.ShipmentId, x.CreatedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<OpsAlert>(e =>
        {
            e.ToTable("alerts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.SubjectKey).HasMaxLength(80);
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.TransferNumber).HasMaxLength(24);
            e.Property(x => x.RequestNumber).HasMaxLength(24);
            e.Property(x => x.HubCode).HasMaxLength(8);
            e.Property(x => x.Governorate).HasMaxLength(5);
            e.Property(x => x.Detail).HasMaxLength(200);
            e.Property(x => x.ResolvedBy).HasMaxLength(256);
            e.Ignore(x => x.IsOpen);

            // One open alert per kind and subject; resolved alerts are kept as history.
            e.HasIndex(x => new { x.TenantId, x.SubjectKey }).IsUnique().HasFilter("resolved_at IS NULL");
            e.HasIndex(x => new { x.TenantId, x.ResolvedAt, x.RaisedAt });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AutomationSettings>(e =>
        {
            e.ToTable("automation_settings");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.OrderConfirmation).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<AutomationLogEntry>(e =>
        {
            e.ToTable("automation_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Rule).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.Detail).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.At });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<CustomerNotification>(e =>
        {
            e.ToTable("customer_notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.TrackingNumber).HasMaxLength(16);
            e.Property(x => x.Template).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.Phone).HasMaxLength(24);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.SentBy).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.ShipmentId });
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<HubScan>(e =>
        {
            e.ToTable("hub_scans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.HubCode).HasMaxLength(8);
            e.Property(x => x.Operation).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TrackingNumber).HasMaxLength(40);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ExpectedHubCode).HasMaxLength(8);
            e.Property(x => x.WarehouseBin).HasMaxLength(20);
            e.Property(x => x.TransferNumber).HasMaxLength(24);
            e.Property(x => x.CourierName).HasMaxLength(100);
            e.Property(x => x.Message).HasMaxLength(300);
            e.Property(x => x.ProblemCode).HasMaxLength(60);
            e.Property(x => x.ScannedBy).HasMaxLength(256);
            e.HasIndex(x => new { x.TenantId, x.Id });
            e.HasIndex(x => new { x.TenantId, x.HubCode, x.ScannedAt });
            e.HasIndex(x => new { x.TenantId, x.ScannedAt }); // the radar counts today's scans across all hubs every few seconds
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<RateCardSettings>(e =>
        {
            e.ToTable("rate_cards");
            e.HasKey(x => x.TenantId);
            e.Property(x => x.SameGovernorateFee).HasPrecision(12, 2);
            e.Property(x => x.DefaultFee).HasPrecision(12, 2);
            e.Property(x => x.IncludedWeightKg).HasPrecision(8, 2);
            e.Property(x => x.ExtraKgFee).HasPrecision(12, 2);
            e.Property(x => x.ReturnFeePercent).HasPrecision(5, 2);
            e.Property(x => x.GovernorateFees).HasColumnType("jsonb");
            e.Property(x => x.SellerDiscounts).HasColumnType("jsonb");
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });

        modelBuilder.Entity<Warehouse>(e =>
        {
            e.ToTable("warehouses");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(8);
            e.Property(x => x.RoutingTag).HasMaxLength(3);
            e.Property(x => x.NameAr).HasMaxLength(120);
            e.Property(x => x.NameEn).HasMaxLength(120);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Governorate).HasMaxLength(5);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.ManagerName).HasMaxLength(120);
            e.Property(x => x.OpeningHours).HasMaxLength(120);
            e.Property(x => x.Covers).HasColumnType("text[]");
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.UpdatedBy).HasMaxLength(256);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.IsPrimary }).IsUnique().HasFilter("is_primary");
            e.HasQueryFilter(x => x.TenantId == CurrentTenantId);
        });
    }
}
