# Flat Kharch Khata

Monthly flat expense tracker: roz kharch, chai & vella, bills, payments, cash, and each flatmate's balance.
ASP.NET Core (.NET 9) website with a SQL Server database.

## Run it

```
cd FlatKharchKhata
dotnet run
```

Then open http://localhost:5080

The first time it runs, it creates the `FlatKharchKhata` database and its tables (`Data/schema.sql`),
and loads September 2026 from the Excel sheet plus a blank October 2026 (`Data/seed.json`).

## Settings (`appsettings.json`)

- `ConnectionStrings:Khata`: the SQL Server connection. The default uses your Windows login on `localhost`.
- `App:Password`: leave empty for no login (fine on your own PC). **Set it before putting the site online**;
  everyone then signs in with that one password.
- `Urls`: where the site listens. Use `http://0.0.0.0:5080` to open it from your phone on the same Wi-Fi
  (`http://<this PC's IP>:5080`). You may need to allow port 5080 in Windows Firewall.

## Tables

| Table | What it holds |
|---|---|
| `Months` | One row per month, plus Borchi's vella/chai, bottle price, Borchi Kharchi per day |
| `People` | Flatmates for each month |
| `DailyKharch` | Daily entries. `Roz` keeps what was typed (e.g. `520+1700`); `RozAmount` is the worked-out number |
| `MealCounts` | Chai and vella per person per day |
| `Bills` | Fixed bills (rent, electricity, gas, wifi, Borchi salary, maintenance…) |
| `Payments` | Money each flatmate paid |
| `CashEntries` | Cash in / cash out |

## Putting it online

Vercel can't run .NET or SQL Server. Use a Windows/.NET host instead, for example:
- **IIS on this PC or another Windows server**: `dotnet publish -c Release`, then point an IIS site at the publish folder
  (install the .NET 9 Hosting Bundle first). Give the IIS app pool's user access to the database, or switch the
  connection string to a SQL login.
- **A .NET hosting provider** (Azure App Service, MonsterASP.NET, SmarterASP.NET, etc.) with a SQL Server database:
  change the connection string to the one they give you. The app creates the tables itself on first run.
