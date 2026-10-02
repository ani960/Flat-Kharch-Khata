# Flat Kharch Khata

Flat Kharch Khata is an ASP.NET Core 9 web application for tracking monthly flat expenses, people, bills, payments, cash entries, daily expenses, and meal counts.

## Stack

- ASP.NET Core 9
- PostgreSQL
- Npgsql
- HTML/CSS/JavaScript frontend
- Docker / Render deployment

## Local run

1. Install .NET 9 SDK and PostgreSQL.
2. Set `ConnectionStrings:Khata` in `appsettings.json` to a PostgreSQL connection string.
3. Run:

```bash
dotnet restore
dotnet run
```

The app creates its tables automatically and loads `Data/seed.json` when the database is empty.

## Render deployment

Create a Render PostgreSQL database and a Render Web Service from this repository using the included `Dockerfile`.

Set these environment variables on the Render Web Service:

- `ConnectionStrings__Khata` = the Render PostgreSQL **internal connection URL** (recommended when the database and web service are in the same Render region)
- `App__Password` = a strong password for the application's shared login

The app also accepts the PostgreSQL URL through `DATABASE_URL`.

Render's web service must listen on `0.0.0.0`; this project uses port `10000` by default.
