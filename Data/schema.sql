-- Flat Kharch Khata schema (SQL Server 2008 R2 and later).
-- The app runs this automatically at startup; every table is created only if it is missing.
-- Text columns like Roz / Amount keep what was typed (e.g. '520+1700'); the *Amount / *Value columns hold the worked-out number.

IF OBJECT_ID('dbo.Months', 'U') IS NULL
CREATE TABLE dbo.Months (
    MonthKey     CHAR(7)        NOT NULL PRIMARY KEY,          -- e.g. '2026-10'
    CookMeals    DECIMAL(18, 2) NOT NULL DEFAULT 60,           -- Borchi's own vella added to the total
    CookTeas     DECIMAL(18, 2) NOT NULL DEFAULT 60,           -- Borchi's own chai added to the total
    BottlePrice  DECIMAL(18, 2) NOT NULL DEFAULT 140,
    BorchiDaily  DECIMAL(18, 2) NOT NULL DEFAULT 50,
    Version      INT            NOT NULL DEFAULT 1,
    CreatedAt    DATETIME       NOT NULL DEFAULT GETDATE(),
    UpdatedAt    DATETIME       NOT NULL DEFAULT GETDATE()
);

IF OBJECT_ID('dbo.People', 'U') IS NULL
CREATE TABLE dbo.People (
    MonthKey   CHAR(7)       NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    PersonId   NVARCHAR(64)  NOT NULL,
    Name       NVARCHAR(100) NOT NULL DEFAULT '',
    SortOrder  INT           NOT NULL DEFAULT 0,
    PRIMARY KEY (MonthKey, PersonId)
);

IF OBJECT_ID('dbo.DailyKharch', 'U') IS NULL
CREATE TABLE dbo.DailyKharch (
    MonthKey      CHAR(7)        NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    Day           TINYINT        NOT NULL,
    Description   NVARCHAR(500)  NOT NULL DEFAULT '',
    Roz           NVARCHAR(200)  NOT NULL DEFAULT '',
    RozAmount     DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Milk          NVARCHAR(200)  NOT NULL DEFAULT '',
    MilkAmount    DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Barf          NVARCHAR(200)  NOT NULL DEFAULT '',
    BarfAmount    DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Tanker        NVARCHAR(200)  NOT NULL DEFAULT '',
    TankerAmount  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Bottles       NVARCHAR(200)  NOT NULL DEFAULT '',
    BottlesCount  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    Borchi        NVARCHAR(200)  NOT NULL DEFAULT '',
    BorchiAmount  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    PRIMARY KEY (MonthKey, Day)
);

IF OBJECT_ID('dbo.MealCounts', 'U') IS NULL
CREATE TABLE dbo.MealCounts (
    MonthKey  CHAR(7)      NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    Day       TINYINT      NOT NULL,
    PersonId  NVARCHAR(64) NOT NULL,
    Chai      INT          NOT NULL DEFAULT 0,
    Vella     INT          NOT NULL DEFAULT 0,
    PRIMARY KEY (MonthKey, Day, PersonId)
);

IF OBJECT_ID('dbo.Bills', 'U') IS NULL
CREATE TABLE dbo.Bills (
    MonthKey     CHAR(7)        NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    BillId       NVARCHAR(64)   NOT NULL,
    Name         NVARCHAR(100)  NOT NULL DEFAULT '',
    Amount       NVARCHAR(200)  NOT NULL DEFAULT '',
    AmountValue  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    SortOrder    INT            NOT NULL DEFAULT 0,
    PRIMARY KEY (MonthKey, BillId)
);

IF OBJECT_ID('dbo.Payments', 'U') IS NULL
CREATE TABLE dbo.Payments (
    MonthKey     CHAR(7)        NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    PaymentId    NVARCHAR(64)   NOT NULL,
    PersonId     NVARCHAR(64)   NOT NULL DEFAULT '',
    Amount       NVARCHAR(200)  NOT NULL DEFAULT '',
    AmountValue  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    PayDate      DATE           NULL,
    Note         NVARCHAR(300)  NOT NULL DEFAULT '',
    PRIMARY KEY (MonthKey, PaymentId)
);

IF OBJECT_ID('dbo.CashEntries', 'U') IS NULL
CREATE TABLE dbo.CashEntries (
    MonthKey     CHAR(7)        NOT NULL REFERENCES dbo.Months (MonthKey) ON DELETE CASCADE,
    CashId       NVARCHAR(64)   NOT NULL,
    EntryType    NVARCHAR(3)    NOT NULL DEFAULT 'in',         -- 'in' or 'out'
    Amount       NVARCHAR(200)  NOT NULL DEFAULT '',
    AmountValue  DECIMAL(18, 2) NOT NULL DEFAULT 0,
    EntryDate    DATE           NULL,
    Note         NVARCHAR(300)  NOT NULL DEFAULT '',
    PRIMARY KEY (MonthKey, CashId)
);
