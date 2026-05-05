# WMS Database Schema Reference

ระบบมี 12 schemas ใน Tenant DB + System tables ใน Master DB

---

## 🗄️ MASTER DB (System-level, shared across tenants)

```sql
-- ⭐ Tenants (multi-tenant routing)
CREATE TABLE master.Tenants (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20) UNIQUE NOT NULL,
    Name            NVARCHAR(200),
    DatabaseName    VARCHAR(50),
    ConnectionStringEncrypted VARBINARY(MAX),
    Status          VARCHAR(20),  -- Active/Suspended/Inactive
    SubscriptionTier VARCHAR(20),
    CreatedAt       DATETIME2,
    INDEX IX_Tenants_Status (Status, Code)
);

-- ⭐ User → Tenant routing (which tenants can user access)
CREATE TABLE master.UserTenantMap (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    UserEmail       VARCHAR(100),
    TenantId        UNIQUEIDENTIFIER,
    Role            VARCHAR(20),
    IsDefault       BIT,
    INDEX IX_UserTenant (UserEmail, TenantId)
);

-- ⭐ Super admin (manages tenants, separate auth)
CREATE TABLE master.SuperAdmins (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Email           VARCHAR(100) UNIQUE,
    PasswordHash    VARCHAR(500),
    Permissions     NVARCHAR(MAX)
);

-- ⭐ Login attempts (rate limiting, security)
CREATE TABLE master.LoginAttempts (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Email           VARCHAR(100),
    IpAddress       VARCHAR(45),
    Success         BIT,
    AttemptedAt     DATETIME2 DEFAULT SYSUTCDATETIME(),
    INDEX IX_LoginAttempts (Email, AttemptedAt DESC)
);

-- ⭐ Pre-auth tokens (between login → tenant selection)
CREATE TABLE master.PreAuthTokens (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    UserEmail       VARCHAR(100),
    Token           VARCHAR(500),
    ExpiresAt       DATETIME2,
    UsedAt          DATETIME2 NULL,
    INDEX IX_PreAuth (Token, ExpiresAt)
);

-- ⭐ System audit (cross-tenant actions)
CREATE TABLE master.SystemAuditLog (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    EventType       VARCHAR(50),
    UserId          UNIQUEIDENTIFIER,
    TenantId        UNIQUEIDENTIFIER NULL,
    Details         NVARCHAR(MAX),  -- JSON
    Timestamp       DATETIME2
);
```

---

## 🗄️ TENANT DB Schemas

### Schema 1: master (Physical Layout, Business Entities, Products, Configuration)

```sql
-- ⭐ Warehouses
CREATE TABLE master.Warehouses (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20) UNIQUE,
    Name            NVARCHAR(200),
    Address         NVARCHAR(500),
    Type            VARCHAR(20),  -- Main/Satellite/Branch
    IsActive        BIT
);

-- ⭐ Warehouse docks (inbound/outbound)
CREATE TABLE master.WarehouseDocks (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    WarehouseId     UNIQUEIDENTIFIER,
    Code            VARCHAR(20),  -- DOCK-IN-01
    Type            VARCHAR(20),  -- Receiving/Shipping/Both
    Status          VARCHAR(20)
);

-- ⭐ Zones (warehouse areas)
CREATE TABLE master.Zones (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    WarehouseId     UNIQUEIDENTIFIER,
    Code            VARCHAR(20),  -- 'Cold', 'Bulk', 'Pickface', 'Quarantine'
    Name            NVARCHAR(100),
    Type            VARCHAR(30),
    Temperature     VARCHAR(20),  -- Ambient/Chilled/Frozen
    AllowedProductTypes NVARCHAR(500),  -- restrictions
    LotCommingleAllowed BIT
);

-- ⭐ Locations (with capacity + rank from BC pattern + 3D coords)
CREATE TABLE master.Locations (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ZoneId          UNIQUEIDENTIFIER,
    Code            VARCHAR(20),  -- 'A-12-3'
    Description     NVARCHAR(200),
    
    -- Physical dimensions
    LengthCm        DECIMAL(10,2),
    WidthCm         DECIMAL(10,2),
    HeightCm        DECIMAL(10,2),
    
    -- Capacity (from BC pattern)
    CapacityVolumeCubicCm   DECIMAL(18,2),
    CapacityWeightKg        DECIMAL(18,2),
    CapacityPolicy          VARCHAR(20),  -- NoLimit/ByVolume/ByWeight/ByEither/ByBoth
    
    -- Rank (from BC pattern)
    BinRank         INT NOT NULL DEFAULT 100,
    
    -- Bin behavior
    AllowMultipleItems  BIT NOT NULL DEFAULT 1,
    
    -- Min/Max thresholds
    MinPickQty      DECIMAL(18,4),
    MaxPickQty      DECIMAL(18,4),
    
    -- ⭐ 3D Coordinates (Phase 1 schema, Phase 4 visualization)
    -- In meters from warehouse origin
    PositionX       DECIMAL(10,3) NULL,  -- left-right
    PositionY       DECIMAL(10,3) NULL,  -- forward-back (depth)
    PositionZ       DECIMAL(10,3) NULL,  -- vertical (rack level)
    Rotation        DECIMAL(5,2) DEFAULT 0,  -- degrees, for non-axis-aligned bins
    
    -- ⭐ Grouping for visualization
    Aisle           VARCHAR(10),   -- 'A', 'B', 'C'
    Bay             INT,           -- 1, 2, 3
    Level           INT,           -- 1=ground, 2=mid, 3=top
    
    -- ⭐ Display metadata
    Show3D          BIT NOT NULL DEFAULT 1,
    DisplayColor    VARCHAR(20) NULL,  -- override for special zones
    IsPickface      BIT NOT NULL DEFAULT 0,
    
    Status          VARCHAR(20),  -- Active/Blocked/Maintenance
    LastVerifiedAt  DATETIME2,
    
    INDEX IX_Location_Zone (ZoneId, Code),
    INDEX IX_Location_Rank (BinRank)
);

-- ⭐ Bin Contents (Fixed/Floating bins from BC)
CREATE TABLE master.BinContents (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    LocationId      UNIQUEIDENTIFIER NOT NULL,
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    UomId           UNIQUEIDENTIFIER NULL,
    
    IsFixed         BIT NOT NULL DEFAULT 0,
    IsDefault       BIT NOT NULL DEFAULT 0,
    IsDedicated     BIT NOT NULL DEFAULT 0,
    
    MinQty          DECIMAL(18,4) NULL,
    MaxQty          DECIMAL(18,4) NULL,
    ReorderPoint    DECIMAL(18,4) NULL,
    ReorderQty      DECIMAL(18,4) NULL,
    
    EffectiveFrom   DATE,
    EffectiveTo     DATE NULL,
    IsActive        BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT UQ_BinContent UNIQUE (LocationId, ProductId, UomId)
);

-- ⭐ Putaway Templates (cascading rules from BC)
CREATE TABLE master.PutawayTemplates (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20) UNIQUE,
    Name            NVARCHAR(100),
    WarehouseId     UNIQUEIDENTIFIER NULL,
    IsDefault       BIT,
    IsActive        BIT
);

CREATE TABLE master.PutawayTemplateLines (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    TemplateId      UNIQUEIDENTIFIER,
    LineNumber      INT,
    
    FindFixedBin            BIT,
    FindFloatingBin         BIT,
    FindBinWithSameItem     BIT,
    FindEmptyBin            BIT,
    
    CheckCapacity           BIT,
    CheckMinQty             BIT,
    CheckMaxQty             BIT,
    
    MatchUom                BIT,
    MatchLot                BIT,
    
    AllowedZones            NVARCHAR(500),
    RuleDescription         NVARCHAR(500),
    
    INDEX IX_TemplateLine (TemplateId, LineNumber)
);

-- ⭐ Pack Stations
CREATE TABLE master.PackStations (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- PACK-01
    WarehouseId     UNIQUEIDENTIFIER,
    Type            VARCHAR(20),  -- Standard/Express/Returns
    
    HasScale        BIT,
    HasWebcam       BIT,
    VideoEnabled    BIT,
    HasPrinter      BIT,
    
    Status          VARCHAR(20)
);

-- ⭐ Container Types
CREATE TABLE master.ContainerTypes (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- 'Pallet', 'BoxL', 'BoxM', 'BoxS', 'Tote'
    Name            NVARCHAR(50),
    
    LengthCm        DECIMAL(10,2),
    WidthCm         DECIMAL(10,2),
    HeightCm        DECIMAL(10,2),
    VolumeCubicCm   DECIMAL(18,2),
    MaxWeightKg     DECIMAL(10,2),
    
    Type            VARCHAR(20),
    IsReusable      BIT
);

-- ⭐ Box Types (shipping)
CREATE TABLE master.BoxTypes (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- BOX-S, BOX-M, BOX-L, BOX-XL
    Name            NVARCHAR(50),
    
    InternalLengthCm    DECIMAL(10,2),
    InternalWidthCm     DECIMAL(10,2),
    InternalHeightCm    DECIMAL(10,2),
    InternalVolumeCubicCm DECIMAL(18,2),
    
    ExternalLengthCm    DECIMAL(10,2),
    ExternalWidthCm     DECIMAL(10,2),
    ExternalHeightCm    DECIMAL(10,2),
    
    EmptyWeightKg       DECIMAL(10,3),
    MaxLoadKg           DECIMAL(10,2),
    
    UnitCost            DECIMAL(10,2),
    
    IsActive            BIT
);

-- ⭐ Owners (Self/Supplier/VMI/3PL Customer)
CREATE TABLE master.Owners (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20) UNIQUE,
    Name            NVARCHAR(200),
    LegalName       NVARCHAR(200),
    
    OwnerType       VARCHAR(20),  -- Self/Supplier/VMI/Customer3PL/Owner
    
    ContactName     NVARCHAR(100),
    Email           VARCHAR(100),
    Phone           VARCHAR(20),
    Address         NVARCHAR(500),
    TaxId           VARCHAR(20),
    
    HasPortalAccess     BIT,
    SettlementFrequency VARCHAR(20),
    SettlementPriceModel VARCHAR(20),
    PaymentTerms        VARCHAR(50),
    
    StorageRatePerPalletDay DECIMAL(10,2) NULL,
    HandlingRatePerOrder    DECIMAL(10,2) NULL,
    
    IsActive            BIT,
    INDEX IX_Owners_Type (OwnerType, IsActive)
);

-- ⭐ Product → Owner mapping (multi-source SKU)
CREATE TABLE master.ProductOwners (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    OwnerId         UNIQUEIDENTIFIER,
    
    OwnerSku        VARCHAR(50),
    OwnerBarcode    VARCHAR(50),
    
    CostBasis       DECIMAL(18,4) NULL,
    SettlementPrice DECIMAL(18,4) NULL,
    Currency        VARCHAR(3),
    
    IsPreferred     BIT,
    IsActive        BIT,
    EffectiveFrom   DATE,
    EffectiveTo     DATE NULL,
    
    LeadTimeDays    INT,
    MinOrderQty     DECIMAL(18,4),
    
    INDEX IX_ProductOwner (ProductId, IsActive),
    INDEX IX_OwnerProduct (OwnerId, ProductId)
);

-- ⭐ Customers (B2B + B2C)
CREATE TABLE master.Customers (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),
    Name            NVARCHAR(200),
    CustomerType    VARCHAR(20),  -- B2B/B2C
    
    -- B2B fields
    CompanyName     NVARCHAR(200),
    TaxId           VARCHAR(20),
    
    -- Contact
    Email           VARCHAR(100),
    Phone           VARCHAR(20),
    
    -- Customer Tier (from TrueCommerce)
    CustomerTier        VARCHAR(10),  -- Tier1-4
    AnnualRevenue       DECIMAL(18,2),
    OrdersPerMonth      INT,
    AvgOrderValue       DECIMAL(18,2),
    IsKeyAccount        BIT,
    IsStrategic         BIT,
    AllocationPriority  INT,
    SafetyStockDays     INT,
    PromisedFillRate    DECIMAL(5,2),
    PreferredCarrierId  UNIQUEIDENTIFIER NULL,
    DefaultPaymentTerms VARCHAR(50),
    
    Status          VARCHAR(20),
    
    INDEX IX_Customer_Tier (CustomerTier, AllocationPriority)
);

-- ⭐ Channels (Manual/Shopee/Lazada/TikTok/B2B-Portal)
CREATE TABLE master.Channels (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),
    Name            NVARCHAR(50),
    Type            VARCHAR(20),  -- Marketplace/Direct/B2B
    IsActive        BIT
);

-- ⭐ Order Sources
CREATE TABLE master.OrderSources (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- NORMAL/EDI/WEB/PORTAL/PHONE/EMAIL/IMPORT/API
    Name            NVARCHAR(50),
    RequiresApproval BIT,
    AllowAutoSync   BIT
);

-- ⭐ Carriers + configurations
CREATE TABLE master.Carriers (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- Flash/Kerry/JT/ThaiPost
    Name            NVARCHAR(100),
    Status          VARCHAR(20),  -- Inactive/Configured/Tested/Production
    IsActive        BIT
);

CREATE TABLE master.CarrierConfigs (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    CarrierId       UNIQUEIDENTIFIER,
    
    ApiEndpoint     VARCHAR(500),
    ApiKeyEncrypted VARBINARY(MAX),
    Mode            VARCHAR(20),  -- Eager/Deferred
    
    HealthStatus    VARCHAR(20),  -- Healthy/Degraded/Down
    LastHealthCheck DATETIME2
);

CREATE TABLE master.CarrierCoverage (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    CarrierId       UNIQUEIDENTIFIER,
    Province        NVARCHAR(50),
    District        NVARCHAR(50),
    ServiceLevel    VARCHAR(20),
    DeliveryDays    INT,
    IsActive        BIT
);

CREATE TABLE master.CarrierRates (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    CarrierId       UNIQUEIDENTIFIER,
    Zone            VARCHAR(20),
    WeightFromKg    DECIMAL(10,2),
    WeightToKg      DECIMAL(10,2),
    Price           DECIMAL(10,2),
    EffectiveFrom   DATE,
    EffectiveTo     DATE NULL,
    INDEX IX_CarrierRates_Active (CarrierId, EffectiveFrom, EffectiveTo)
);

-- ⭐ Marketplace configurations
CREATE TABLE master.MarketplaceConfigs (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- SHOPEE/LAZADA/TIKTOK
    Name            NVARCHAR(100),
    
    ApiEndpoint     VARCHAR(500),
    ApiCredentialsEncrypted VARBINARY(MAX),
    
    SyncSettings    NVARCHAR(MAX),  -- JSON
    SafetyStockBufferPct DECIMAL(5,2),  -- 10%
    
    Status          VARCHAR(20)
);

CREATE TABLE master.MarketplaceSkuMappings (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    MarketplaceId   UNIQUEIDENTIFIER,
    ExternalSku     VARCHAR(100),
    ExternalProductId VARCHAR(100),
    LastSyncAt      DATETIME2,
    INDEX IX_SkuMapping (MarketplaceId, ExternalSku)
);

-- ⭐ Video Recording Policies
CREATE TABLE master.VideoRecordingPolicies (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    PackStationId   UNIQUEIDENTIFIER NULL,
    ChannelId       UNIQUEIDENTIFIER NULL,
    OrderType       VARCHAR(20) NULL,
    
    EnableRecording BIT,
    RetentionDays   INT,
    
    IsActive        BIT
);

-- ⭐ Product Categories
CREATE TABLE master.ProductCategories (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),
    Name            NVARCHAR(100),
    ParentId        UNIQUEIDENTIFIER NULL,
    Path            VARCHAR(500),  -- /Electronics/Mobile
    IsActive        BIT
);

-- ⭐ Units of Measure
CREATE TABLE master.UnitsOfMeasure (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20),  -- 'EA', 'PACK', 'CASE', 'KG', 'L'
    Name            NVARCHAR(50),
    Type            VARCHAR(20),  -- Count/Weight/Volume/Length
    IsBase          BIT
);

-- ⭐ UoM Conversions (with dimensions per UoM)
CREATE TABLE master.UomConversions (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    FromUomId       UNIQUEIDENTIFIER,
    ToUomId         UNIQUEIDENTIFIER,
    Factor          DECIMAL(18,6),
    
    -- Dimensions per UoM
    LengthCm        DECIMAL(10,2),
    WidthCm         DECIMAL(10,2),
    HeightCm        DECIMAL(10,2),
    VolumeCubicCm   DECIMAL(18,2),
    WeightKg        DECIMAL(10,3),
    
    INDEX IX_UomConversion (ProductId, FromUomId, ToUomId)
);

-- ⭐ Products
CREATE TABLE master.Products (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(50) UNIQUE,
    Name            NVARCHAR(200),
    CategoryId      UNIQUEIDENTIFIER,
    
    BaseUomId       UNIQUEIDENTIFIER,
    
    -- Tracking
    TrackingMethod  VARCHAR(20),  -- None/Lot/LotAndSerial
    
    -- ABC velocity (computed periodically)
    VelocityClass   VARCHAR(2),
    
    -- Catch weight
    UseCatchWeight  BIT,
    
    Status          VARCHAR(20),
    INDEX IX_Product_Category (CategoryId, Status)
);

CREATE TABLE master.ProductBarcodes (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    Barcode         VARCHAR(50) UNIQUE,
    UomId           UNIQUEIDENTIFIER,
    IsPrimary       BIT
);

CREATE TABLE master.ProductPackingConfigs (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    UomId           UNIQUEIDENTIFIER,
    SuggestedBoxTypeId UNIQUEIDENTIFIER NULL,
    PackMode        VARCHAR(20)  -- ScanEach/ScanAndQty
);

-- ⭐ Strategy configurations (from TrueCommerce + this design)
CREATE TABLE master.AllocationStrategies (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NULL,
    CategoryId      UNIQUEIDENTIFIER NULL,
    ChannelId       UNIQUEIDENTIFIER NULL,
    CustomerId      UNIQUEIDENTIFIER NULL,
    
    AllocationApproach VARCHAR(20),  -- Push/Pull/JIT/Hybrid
    RotationMethod  VARCHAR(20),  -- FIFO/FEFO/LIFO/LotPriority
    ReservationType VARCHAR(20),  -- Hard/Soft/None
    SoftReserveAt   VARCHAR(30),
    HardReserveAt   VARCHAR(30),
    
    Priority        INT,
    EffectiveFrom   DATE,
    EffectiveTo     DATE NULL,
    IsActive        BIT
);

CREATE TABLE master.SlottingStrategies (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    WarehouseId     UNIQUEIDENTIFIER,
    StrategyType    VARCHAR(30),
    AClassZones     VARCHAR(100),
    BClassZones     VARCHAR(100),
    CClassZones     VARCHAR(100),
    GroupByCategory BIT,
    GroupBySupplier BIT,
    ReslotIntervalDays INT,
    LastReslotAt    DATETIME2,
    IsActive        BIT
);

CREATE TABLE master.PickingStrategies (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    WarehouseId     UNIQUEIDENTIFIER,
    StrategyType    VARCHAR(30),  -- Discrete/Batch/Zone/ZoneBatch/Wave
    UseZonePicking  BIT,
    ConsolidationStation VARCHAR(50),
    TriggerCondition VARCHAR(50),
    IsActive        BIT
);

CREATE TABLE master.ReplenishmentRules (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER NULL,
    LocationId      UNIQUEIDENTIFIER NULL,
    
    MinQty          DECIMAL(18,4),
    MaxQty          DECIMAL(18,4),
    ReorderPoint    DECIMAL(18,4),
    ReorderQty      DECIMAL(18,4),
    
    Strategy        VARCHAR(30),  -- MinMax/WaveTrigger/OnDemand/Scheduled
    PreferredSourceLocationId UNIQUEIDENTIFIER NULL,
    Priority        INT,
    IsActive        BIT
);

CREATE TABLE master.CrossDockRules (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    TriggerType     VARCHAR(30),
    SkipPutaway     BIT,
    StagingLocation UNIQUEIDENTIFIER,
    AutoConvertToShipment BIT,
    Priority        INT,
    IsActive        BIT
);

-- ⭐ Item Stratification (from TrueCommerce)
CREATE TABLE master.ItemStratification (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER UNIQUE,
    
    VelocityClass       VARCHAR(2),
    MarginClass         VARCHAR(2),
    StrategicClass      VARCHAR(2),
    LeadTimeClass       VARCHAR(2),
    ShelfLifeClass      VARCHAR(2),
    VariabilityClass    VARCHAR(2),
    
    OverallTier         VARCHAR(10),
    SafetyStockDays     INT,
    PreferredFulfillment VARCHAR(20),
    AllocationPriority  INT,
    
    AvgDailySales       DECIMAL(18,4),
    DemandVariability   DECIMAL(18,4),
    Margin              DECIMAL(18,4),
    
    LastReviewedAt      DATETIME2,
    NextReviewAt        DATETIME2
);

-- ⭐ Warehouse Capacity (from TrueCommerce)
CREATE TABLE master.WarehouseCapacity (
    WarehouseId             UNIQUEIDENTIFIER PRIMARY KEY,
    TotalVolumeCubicM       DECIMAL(18,2),
    TotalWeightKg           DECIMAL(18,2),
    TotalPalletPositions    INT,
    UsableVolumeCubicM      DECIMAL(18,2),
    
    CurrentVolumeCubicM     DECIMAL(18,2),
    CurrentWeightKg         DECIMAL(18,2),
    CurrentPalletPositions  INT,
    
    WarningThresholdPct     INT,
    CriticalThresholdPct    INT,
    UpdatedAt               DATETIME2
);

-- ⭐ Other configurations
CREATE TABLE master.CountVarianceRules (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Threshold       DECIMAL(18,4),
    Severity        VARCHAR(20),
    RequireRecount  BIT,
    AutoApprove     BIT,
    NotifyRoles     NVARCHAR(500)
);

CREATE TABLE master.SystemSettings (
    Key             VARCHAR(100) PRIMARY KEY,
    Value           NVARCHAR(MAX),
    Category        VARCHAR(50),
    Description     NVARCHAR(500),
    UpdatedAt       DATETIME2
);

CREATE TABLE master.HolidayCalendar (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Date            DATE,
    Name            NVARCHAR(100),
    Type            VARCHAR(20)  -- Public/Company/Regional
);

CREATE TABLE master.EmailTemplates (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(50),
    Subject         NVARCHAR(200),
    BodyHtml        NVARCHAR(MAX),
    Variables       NVARCHAR(500)
);

CREATE TABLE master.DocumentTemplates (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(50),
    Name            NVARCHAR(100),
    Type            VARCHAR(30),  -- PickList/PackSlip/Invoice/Label
    TemplatePath    VARCHAR(500),
    Format          VARCHAR(20)  -- PDF/XML/HTML
);

CREATE TABLE master.ChangeLog (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    EntityType      VARCHAR(50),
    EntityId        UNIQUEIDENTIFIER,
    Action          VARCHAR(20),
    PreviousData    NVARCHAR(MAX),
    NewData         NVARCHAR(MAX),
    ChangedBy       UNIQUEIDENTIFIER,
    ChangedAt       DATETIME2,
    Reason          NVARCHAR(500)
);
```

---

### Schema 2: inventory (Stock, Lots, Pallets, ATP)

```sql
-- ⭐ Pallets
CREATE TABLE inventory.Pallets (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    PalletNumber    VARCHAR(30) UNIQUE,  -- PL-YYYYMMDD-NNNN
    Status          VARCHAR(20),
    CurrentLocationId UNIQUEIDENTIFIER NULL,
    CreatedAt       DATETIME2
);

-- ⭐ Lots (with Owner)
CREATE TABLE inventory.Lots (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,
    LotNumber       VARCHAR(50),
    ProductId       UNIQUEIDENTIFIER,
    
    ManufactureDate DATE NULL,
    ExpiryDate      DATE NULL,
    SupplierLotNumber VARCHAR(50) NULL,
    
    Status          VARCHAR(20),
    
    CONSTRAINT UQ_Lot UNIQUE (OwnerId, LotNumber)
);

-- ⭐ Serials
CREATE TABLE inventory.Serials (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    SerialNumber    VARCHAR(100) UNIQUE,
    ProductId       UNIQUEIDENTIFIER,
    LotId           UNIQUEIDENTIFIER NULL,
    Status          VARCHAR(20),  -- InStock/Allocated/Shipped/Returned
    CurrentStockId  UNIQUEIDENTIFIER NULL,
    OrderId         UNIQUEIDENTIFIER NULL
);

-- ⭐ Stock (with Owner + Storage age)
CREATE TABLE inventory.Stock (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    LocationId      UNIQUEIDENTIFIER,
    ProductId       UNIQUEIDENTIFIER,
    LotId           UNIQUEIDENTIFIER NULL,
    PalletId        UNIQUEIDENTIFIER NULL,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,
    OwnershipType   VARCHAR(20),  -- Owned/Consigned/Storage
    
    UomId           UNIQUEIDENTIFIER,
    OnHand          DECIMAL(18,4),
    Reserved        DECIMAL(18,4),
    Available       AS (OnHand - Reserved) PERSISTED,
    
    -- Aging (for billing)
    StorageStartDate    DATE,
    GracePeriodEndDate  DATE NULL,
    
    -- Catch weight (if applicable)
    WeightKg        DECIMAL(10,3) NULL,
    
    ReceivedAt      DATETIME2,
    LastMovedAt     DATETIME2,
    
    CONSTRAINT UQ_Stock UNIQUE (LocationId, ProductId, LotId, PalletId, OwnerId, UomId),
    INDEX IX_Stock_Product (ProductId, Available),
    INDEX IX_Stock_Owner (OwnerId, ProductId)
);

-- ⭐ Stock Movements (full audit)
CREATE TABLE inventory.StockMovements (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    StockId         UNIQUEIDENTIFIER,
    
    MovementType    VARCHAR(30),  -- Receive/Putaway/Pick/Adjust/Transfer/Return/Cycle
    
    FromLocationId  UNIQUEIDENTIFIER NULL,
    ToLocationId    UNIQUEIDENTIFIER NULL,
    
    Qty             DECIMAL(18,4),
    UomId           UNIQUEIDENTIFIER,
    
    OwnerId         UNIQUEIDENTIFIER,
    
    -- Reference
    ReferenceType   VARCHAR(30),
    ReferenceId     UNIQUEIDENTIFIER,
    
    PerformedBy     UNIQUEIDENTIFIER,
    PerformedAt     DATETIME2,
    
    INDEX IX_Movements_Stock (StockId, PerformedAt DESC),
    INDEX IX_Movements_Reference (ReferenceType, ReferenceId)
);

-- ⭐ Stock Reservations (with Owner)
CREATE TABLE inventory.StockReservations (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    StockId         UNIQUEIDENTIFIER NULL,  -- NULL for soft reserve
    OwnerId         UNIQUEIDENTIFIER,
    
    Type            VARCHAR(20),  -- Hard/Soft
    
    OrderLineId     UNIQUEIDENTIFIER,
    Qty             DECIMAL(18,4),
    
    ExpiresAt       DATETIME2 NULL,  -- B2C 15-min timeout
    Status          VARCHAR(20),  -- Active/Released/Consumed/Expired
    
    INDEX IX_Reservation_Order (OrderLineId, Status),
    INDEX IX_Reservation_Expires (ExpiresAt) WHERE ExpiresAt IS NOT NULL
);

-- ⭐ Soft Reservations (product-level, marketplace)
CREATE TABLE inventory.ProductSoftReservations (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    OwnerId         UNIQUEIDENTIFIER,
    ChannelId       UNIQUEIDENTIFIER,
    
    Qty             DECIMAL(18,4),
    
    LastSyncAt      DATETIME2,
    INDEX IX_SoftReserve (ProductId, OwnerId)
);

-- ⭐ Supply Pipeline (ATP - from TrueCommerce)
CREATE TABLE inventory.SupplyPipeline (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    OwnerId         UNIQUEIDENTIFIER,
    
    SourceType      VARCHAR(30),  -- PurchaseOrder/Production/Transfer/ExpectedReturn
    SourceReference VARCHAR(50),
    
    Qty             DECIMAL(18,4),
    ExpectedDate    DATE,
    
    ConfidenceLevel VARCHAR(20),  -- Confirmed/High/Medium/Low
    Status          VARCHAR(20),
    
    INDEX IX_Pipeline (ProductId, ExpectedDate, Status)
);

-- ⭐ Demand Pipeline (ATP)
CREATE TABLE inventory.DemandPipeline (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    
    SourceType      VARCHAR(30),  -- OrderReserved/Forecast/Promotion/B2BContract
    SourceReference VARCHAR(50),
    
    Qty             DECIMAL(18,4),
    NeededByDate    DATE,
    
    Priority        VARCHAR(20),
    Status          VARCHAR(20),
    
    INDEX IX_Demand (ProductId, NeededByDate, Status)
);

-- ⭐ Transfer Orders (inter-warehouse) — Header
CREATE TABLE inventory.TransferOrders (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    TransferNumber  VARCHAR(30) UNIQUE,
    
    FromWarehouseId UNIQUEIDENTIFIER,
    ToWarehouseId   UNIQUEIDENTIFIER,
    
    Reason          VARCHAR(50),  -- Rebalance/Replenish/CustomerOrder/Return
    RelatedOrderId  UNIQUEIDENTIFIER NULL,
    Priority        VARCHAR(20),  -- Normal/Urgent/Express
    
    -- Workflow status (9-state)
    Status          VARCHAR(20),
    -- Draft/Submitted/Approved/Picking/Dispatched/InTransit/Receiving/Received/Closed
    -- Side: Cancelled/Lost
    
    -- Timestamps
    RequestedAt     DATETIME2,
    ApprovedAt      DATETIME2 NULL,
    DispatchedAt    DATETIME2 NULL,
    ReceivedAt      DATETIME2 NULL,
    ClosedAt        DATETIME2 NULL,
    
    -- People
    RequestedBy     UNIQUEIDENTIFIER,
    ApprovedBy      UNIQUEIDENTIFIER NULL,
    DispatchedBy    UNIQUEIDENTIFIER NULL,
    ReceivedBy      UNIQUEIDENTIFIER NULL,
    
    -- Logistics
    EstimatedTransitDays INT,
    CarrierId       UNIQUEIDENTIFIER NULL,
    TrackingNumber  VARCHAR(100),
    
    Notes           NVARCHAR(1000),
    
    INDEX IX_Transfer_Status (Status, RequestedAt DESC),
    INDEX IX_Transfer_Wh (FromWarehouseId, ToWarehouseId, Status)
);

-- ⭐ Transfer Order Lines (item details)
CREATE TABLE inventory.TransferOrderLines (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    TransferId      UNIQUEIDENTIFIER NOT NULL,
    LineNumber      INT NOT NULL,
    
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,  -- preserve owner
    LotId           UNIQUEIDENTIFIER NULL,      -- preserve lot
    
    -- Source (FROM warehouse)
    FromLocationId  UNIQUEIDENTIFIER NULL,  -- specific or NULL=any
    
    -- Destination (TO warehouse)  
    ToLocationId    UNIQUEIDENTIFIER NULL,  -- specific or NULL=putaway
    
    -- Quantities
    UomId           UNIQUEIDENTIFIER,
    QtyRequested    DECIMAL(18,4),
    QtyDispatched   DECIMAL(18,4) NULL,  -- actually picked
    QtyReceived     DECIMAL(18,4) NULL,  -- actually arrived
    QtyLossInTransit AS (ISNULL(QtyDispatched,0) - ISNULL(QtyReceived,0)) PERSISTED,
    
    -- Status per line
    Status          VARCHAR(20),  -- Pending/Picked/InTransit/Received/Discrepancy
    
    -- Linkage
    PickTaskId      UNIQUEIDENTIFIER NULL,
    AdjustmentId   UNIQUEIDENTIFIER NULL,  -- if loss in transit
    
    INDEX IX_TransferLine_Header (TransferId, LineNumber),
    INDEX IX_TransferLine_Product (ProductId, Status)
);

-- ⭐ Transfer Status History (audit trail)
CREATE TABLE inventory.TransferStatusHistory (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    TransferId      UNIQUEIDENTIFIER NOT NULL,
    
    FromStatus      VARCHAR(20),
    ToStatus        VARCHAR(20),
    Reason          NVARCHAR(500),
    
    PerformedBy     UNIQUEIDENTIFIER,
    PerformedAt     DATETIME2,
    
    INDEX IX_TransferHist (TransferId, PerformedAt DESC)
);

-- ⭐ Adjustment Reasons (master data)
CREATE TABLE master.AdjustmentReasons (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    Code            VARCHAR(20) UNIQUE,
    -- Examples: DAMAGE-WAREHOUSE, LOSS-PICK, FOUND-LOC, QC-REJECT, MANUAL-FIX
    Name            NVARCHAR(100),
    Category        VARCHAR(20),  
    -- Damage/Loss/Found/QC/Manual/Reclassify
    
    Direction       VARCHAR(10),  -- 'Decrease' or 'Increase' or 'Both'
    
    -- Approval requirements
    RequireApproval BIT NOT NULL DEFAULT 1,
    RequirePhoto    BIT NOT NULL DEFAULT 0,
    AuthorityLevel  VARCHAR(20),  -- 'Supervisor', 'Manager', 'GM'
    AuthorityValueLimit DECIMAL(18,2) NULL,  -- override at certain threshold
    
    -- Billing impact (3PL)
    IsChargeable    BIT NOT NULL DEFAULT 0,
    ChargeAccount   VARCHAR(50),
    
    -- Display
    DisplayColor    VARCHAR(20),  -- for UI badges
    DisplayOrder    INT,
    
    IsActive        BIT NOT NULL DEFAULT 1,
    
    INDEX IX_AdjReason_Active (IsActive, Category, DisplayOrder)
);

-- ⭐ Stock Adjustments (general purpose)
CREATE TABLE inventory.StockAdjustments (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    AdjustmentNumber VARCHAR(30) UNIQUE,  -- ADJ-YYYYMMDD-NNNN
    
    -- What stock affected
    StockId         UNIQUEIDENTIFIER NULL,  -- existing stock (Decrease)
    ProductId       UNIQUEIDENTIFIER NOT NULL,
    LocationId      UNIQUEIDENTIFIER NOT NULL,
    LotId           UNIQUEIDENTIFIER NULL,
    PalletId        UNIQUEIDENTIFIER NULL,
    OwnerId         UNIQUEIDENTIFIER NOT NULL,
    
    -- Quantities
    UomId           UNIQUEIDENTIFIER,
    QtyBefore       DECIMAL(18,4),
    QtyAfter        DECIMAL(18,4),
    QtyDelta        AS (QtyAfter - QtyBefore) PERSISTED,
    
    -- Why
    ReasonId        UNIQUEIDENTIFIER NOT NULL,  -- FK to AdjustmentReasons
    Notes           NVARCHAR(1000),
    PhotoUrls       NVARCHAR(MAX),  -- JSON array of photo URLs
    
    -- Source / context
    SourceType      VARCHAR(30) NULL,  
    -- 'CycleCount'/'Picking'/'Receiving'/'Manual'/'Transfer'
    SourceReferenceId UNIQUEIDENTIFIER NULL,
    
    -- Workflow
    Status          VARCHAR(20),  -- Pending/Approved/Rejected/Applied
    
    -- Submission
    SubmittedBy     UNIQUEIDENTIFIER NOT NULL,
    SubmittedAt     DATETIME2 NOT NULL,
    
    -- Approval
    ApprovedBy      UNIQUEIDENTIFIER NULL,
    ApprovedAt      DATETIME2 NULL,
    ApprovalNotes   NVARCHAR(500),
    
    -- Rejection
    RejectedBy      UNIQUEIDENTIFIER NULL,
    RejectedAt      DATETIME2 NULL,
    RejectionReason NVARCHAR(500),
    
    -- Application (when actually changed stock)
    AppliedAt       DATETIME2 NULL,
    StockMovementId UNIQUEIDENTIFIER NULL,  -- linked movement
    
    -- Billing (3PL)
    IsChargeable    BIT NOT NULL DEFAULT 0,
    ChargedAmount   DECIMAL(18,2) NULL,
    BillableActivityId UNIQUEIDENTIFIER NULL,
    
    INDEX IX_Adj_Status (Status, SubmittedAt DESC),
    INDEX IX_Adj_Stock (StockId, AppliedAt DESC),
    INDEX IX_Adj_Reason (ReasonId, SubmittedAt DESC),
    INDEX IX_Adj_Owner (OwnerId, AppliedAt DESC)
);

-- ⭐ Reslotting tasks
CREATE TABLE inventory.ReslottingTasks (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    ProductId       UNIQUEIDENTIFIER,
    FromLocationId  UNIQUEIDENTIFIER,
    ToLocationId    UNIQUEIDENTIFIER,
    Reason          VARCHAR(50),
    Status          VARCHAR(20),
    Priority        INT,
    GeneratedAt     DATETIME2,
    CompletedAt     DATETIME2 NULL
);

-- ⭐ Replenishment tasks
CREATE TABLE inventory.ReplenishmentTasks (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    FromLocationId  UNIQUEIDENTIFIER,
    ToLocationId    UNIQUEIDENTIFIER,
    ProductId       UNIQUEIDENTIFIER,
    LotId           UNIQUEIDENTIFIER NULL,
    Qty             DECIMAL(18,4),
    TriggerReason   VARCHAR(50),
    Status          VARCHAR(20),
    AssignedTo      UNIQUEIDENTIFIER NULL,
    GeneratedAt     DATETIME2,
    CompletedAt     DATETIME2 NULL
);

-- ⭐ Network stock view (multi-location)
CREATE VIEW inventory.NetworkStock AS
SELECT 
    s.ProductId, s.OwnerId,
    w.Id as WarehouseId, w.Name as WarehouseName,
    SUM(s.OnHand) as OnHand,
    SUM(s.Reserved) as Reserved,
    SUM(s.Available) as Available
FROM inventory.Stock s
JOIN master.Locations l ON s.LocationId = l.Id
JOIN master.Warehouses w ON l.WarehouseId = w.Id
GROUP BY s.ProductId, s.OwnerId, w.Id, w.Name;
```

---

### 3D Warehouse Monitor Tables (Phase 4 — Schema in Phase 1)

```sql
-- ⭐ Warehouse Layout master (overall structure)
-- Add fields when needed; coordinates already in Locations
CREATE TABLE master.WarehouseLayouts (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    WarehouseId     UNIQUEIDENTIFIER NOT NULL,
    
    -- Overall dimensions (meters)
    LengthM         DECIMAL(10,2),
    WidthM          DECIMAL(10,2),
    HeightM         DECIMAL(10,2),
    
    -- Origin reference (for coordinate system)
    OriginDescription NVARCHAR(200),
    -- 'Bottom-left corner of building, ground level'
    
    -- Visualization config
    GridSize        DECIMAL(10,2),  -- 1.0 meter grid lines
    DefaultViewMode VARCHAR(20),    -- 'Occupancy' / 'Velocity' / 'Aging' / 'Heatmap'
    
    -- Optional: 3D model upload (advanced)
    ModelFilePath   VARCHAR(500),  -- glTF/GLB file
    ModelFormat     VARCHAR(20),   -- 'gltf', 'glb', 'fbx'
    
    -- Static elements (walls, columns, doors)
    StaticElements  NVARCHAR(MAX),  -- JSON array
    -- Example: [
    --   {type: "wall", x1: 0, y1: 0, x2: 100, y2: 0, height: 8},
    --   {type: "column", x: 25, y: 25, height: 8, radius: 0.3},
    --   {type: "door", x: 50, y: 0, width: 4, height: 4}
    -- ]
    
    UpdatedAt       DATETIME2,
    UpdatedBy       UNIQUEIDENTIFIER
);

-- ⭐ Per-bin activity tracking (for heatmap visualization)
-- Aggregated daily/hourly from movements
CREATE TABLE analytics.LocationActivity (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    LocationId      UNIQUEIDENTIFIER NOT NULL,
    
    Date            DATE,
    Hour            INT,  -- 0-23 for hourly heatmap
    
    -- Activity counters
    PicksCount          INT DEFAULT 0,
    PutawaysCount       INT DEFAULT 0,
    CountAdjustments    INT DEFAULT 0,
    TotalActivities AS (PicksCount + PutawaysCount) PERSISTED,
    
    -- For heatmap intensity
    HeatScore       DECIMAL(10,2),
    
    INDEX IX_LocActivity (LocationId, Date),
    INDEX IX_Heatmap (Date, HeatScore DESC)
);

-- ⭐ Real-time picker positions (for live operations view)
CREATE TABLE analytics.PickerPositions (
    Id              UNIQUEIDENTIFIER PRIMARY KEY,
    UserId          UNIQUEIDENTIFIER,
    
    -- Current location
    LocationId      UNIQUEIDENTIFIER NULL,
    PositionX       DECIMAL(10,3),
    PositionY       DECIMAL(10,3),
    PositionZ       DECIMAL(10,3),
    
    -- Current task
    ActivePickTaskId UNIQUEIDENTIFIER NULL,
    Status          VARCHAR(20),  -- Active/Break/Offline
    
    UpdatedAt       DATETIME2,
    
    INDEX IX_PickerPosition (UserId, UpdatedAt DESC)
);
-- Updated by mobile app on each scan via SignalR
-- Cleared when picker logs out
```

---

### Schema 3-12: Other Schemas

(Similar detailed schemas for:)

- **inbound**: PurchaseOrders, Lines, ReceivingHeaders, ReceivingLines, PutawayTasks, ContainerOperations
- **inventory** (additional): **TransferOrders, TransferOrderLines, TransferStatusHistory, StockAdjustments**
- **outbound**: Orders, OrderLines, SalesOrderDetails, OrderStatusHistory, Shipments, Waves, PickTasks, WaveContainers, PickAllocations, PickScans, PackTasks, PackVerifications, Packages, Manifests, CarrierShipments, TrackingEvents, PackVideos, PackVideoAccessLog, OrderConsolidation
- **marketplace**: WebhookEvents, ReviewQueue
- **returns**: RmaHeaders, RmaLines, Inspections, RmaStatusHistory
- **counts**: CycleCountBatches, CountTasks, CountDetails, CountAdjustments, CountAuditLog
- **security**: Users, Roles, Functions, RoleFunctionPermissions, FunctionSpecialActions, UserRoles, UserConstraints, UserPermissions, UserWarehouses, UserPreferences, AuditLog
- **vmi**: PendingSettlements, SettlementBatches
- **billing**: RateCards, RateCardLines, RateCardTiers, **AgingBrackets**, **RateCardCategoryRates**, **RateCardCategoryTiers**, PricingConditions, BillableActivities, StorageSnapshots, Invoices, InvoiceLines, Payments, ErpExportLog, CalculationPolicies
- **forecast**: DemandForecasts
- **analytics**: SalesVelocity, DailyOrderSummary, PickerPerformance, WaveCompletionStats, StockAging (view), **LocationActivity**, **PickerPositions**
- **master** (additional): **AdjustmentReasons**

---

**Total tables: ~88+ across all schemas** (added 4 for Transfer + Adjustment)

For complete schema details of these schemas, refer to the detailed conversation history.

---

**End of Database Schema Reference**
