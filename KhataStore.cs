using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace FlatKharchKhata;

public enum Kind { Text, Expr, Int, Date }

/// <summary>Maps one JSON field of the page's month document to a table column.</summary>
public sealed record Field(string Json, string Column, Kind Kind, string? ValueColumn = null, int Max = 200);

/// <summary>A keyed list inside a month (people, bills, payments, cash) and the table that stores it.</summary>
public sealed record Section(string Json, string Table, string IdColumn, Field[] Fields);

/// <summary>
/// Reads and writes months in SQL Server. The page works with one JSON document per month;
/// this class turns that document into table rows and back.
/// A patch is a partial document: listed fields are written, a null entry deletes that row.
/// </summary>
public sealed partial class KhataStore(string connectionString)
{
    [GeneratedRegex(@"^[A-Za-z0-9_\-.~:@+]{1,64}$")] private static partial Regex IdRx();
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")] private static partial Regex MonthRx();
    public static bool ValidMonth(string? key) => key is not null && MonthRx().IsMatch(key);

    private static readonly Section People = new("people", "People", "PersonId",
        [new("name", "Name", Kind.Text, Max: 100), new("order", "SortOrder", Kind.Int)]);
    private static readonly Section Bills = new("bills", "Bills", "BillId",
        [new("name", "Name", Kind.Text, Max: 100), new("amount", "Amount", Kind.Expr, "AmountValue"), new("order", "SortOrder", Kind.Int)]);
    private static readonly Section Payments = new("payments", "Payments", "PaymentId",
        [new("pid", "PersonId", Kind.Text, Max: 64), new("amount", "Amount", Kind.Expr, "AmountValue"), new("date", "PayDate", Kind.Date), new("note", "Note", Kind.Text, Max: 300)]);
    private static readonly Section Cash = new("cash", "CashEntries", "CashId",
        [new("type", "EntryType", Kind.Text, Max: 3), new("amount", "Amount", Kind.Expr, "AmountValue"), new("date", "EntryDate", Kind.Date), new("note", "Note", Kind.Text, Max: 300)]);
    private static readonly Section[] Sections = [People, Bills, Payments, Cash];

    private static readonly Field[] DayFields =
    [
        new("desc", "Description", Kind.Text, Max: 500),
        new("roz", "Roz", Kind.Expr, "RozAmount"),
        new("milk", "Milk", Kind.Expr, "MilkAmount"),
        new("barf", "Barf", Kind.Expr, "BarfAmount"),
        new("tanker", "Tanker", Kind.Expr, "TankerAmount"),
        new("bottles", "Bottles", Kind.Expr, "BottlesCount"),
        new("borchi", "Borchi", Kind.Expr, "BorchiAmount"),
    ];
    private static readonly Field[] CountFields = [new("c", "Chai", Kind.Int), new("m", "Vella", Kind.Int)];

    private static readonly Dictionary<string, string> SettingColumns = new()
    {
        ["cookMeals"] = "CookMeals", ["cookTeas"] = "CookTeas", ["bottlePrice"] = "BottlePrice", ["borchiDaily"] = "BorchiDaily",
    };
    private static readonly string[] ChildTables = ["People", "DailyKharch", "MealCounts", "Bills", "Payments", "CashEntries"];

    // ---------- setup ----------

    /// <summary>Creates the database and tables if missing, and loads the seed months into an empty database.</summary>
    public void EnsureDatabase(string dataFolder)
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var dbName = b.InitialCatalog;
        b.InitialCatalog = "master";
        using (var master = new SqlConnection(b.ConnectionString))
        {
            master.Open();
            Exec(master, null, "IF DB_ID(@n) IS NULL BEGIN DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@n); EXEC (@sql); END", ("@n", dbName));
        }

        using var c = Open();
        Exec(c, null, File.ReadAllText(Path.Combine(dataFolder, "schema.sql")));

        var seedPath = Path.Combine(dataFolder, "seed.json");
        if (Scalar(c, null, "SELECT COUNT(*) FROM dbo.Months") is 0 && File.Exists(seedPath))
        {
            var seed = JsonNode.Parse(File.ReadAllText(seedPath))!.AsObject();
            foreach (var (key, doc) in seed)
                if (ValidMonth(key) && doc is JsonObject o) Create(key, o);
        }
    }

    // ---------- reads ----------

    public Dictionary<string, JsonObject> GetAll()
    {
        using var c = Open();
        var result = new Dictionary<string, JsonObject>();
        foreach (var key in MonthKeys(c))
            if (Read(c, key) is { } doc) result[key] = doc;
        return result;
    }

    public JsonObject? Get(string key)
    {
        using var c = Open();
        return Read(c, key);
    }

    public Dictionary<string, int> Versions()
    {
        using var c = Open();
        using var cmd = new SqlCommand("SELECT MonthKey, Version FROM dbo.Months", c);
        using var r = cmd.ExecuteReader();
        var result = new Dictionary<string, int>();
        while (r.Read()) result[r.GetString(0)] = r.GetInt32(1);
        return result;
    }

    // ---------- writes ----------

    /// <summary>Creates a month from a full document. Returns false if the month already exists.</summary>
    public bool Create(string key, JsonObject doc)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (Scalar(c, tx, "SELECT COUNT(*) FROM dbo.Months WITH (UPDLOCK, HOLDLOCK) WHERE MonthKey = @k", ("@k", key)) is not 0)
        {
            tx.Rollback();
            return false;
        }
        Exec(c, tx, "INSERT INTO dbo.Months (MonthKey) VALUES (@k)", ("@k", key));
        ApplyInto(c, tx, key, doc);
        tx.Commit();
        return true;
    }

    /// <summary>Applies a partial document to an existing month. Returns the new version, or null if the month doesn't exist.</summary>
    public int? Patch(string key, JsonObject patch)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        // Bumping the version first locks the month row, so two saves to one month run one after the other.
        var version = Scalar(c, tx, "UPDATE dbo.Months SET Version = Version + 1, UpdatedAt = GETDATE() OUTPUT inserted.Version WHERE MonthKey = @k", ("@k", key));
        if (version is null)
        {
            tx.Rollback();
            return null;
        }
        ApplyInto(c, tx, key, patch);
        tx.Commit();
        return version;
    }

    /// <summary>Replaces a month completely (used when restoring a backup). Creates it if missing.</summary>
    public int Replace(string key, JsonObject doc)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var version = Scalar(c, tx, "UPDATE dbo.Months SET Version = Version + 1, UpdatedAt = GETDATE(), CookMeals = 60, CookTeas = 60, BottlePrice = 140, BorchiDaily = 50 OUTPUT inserted.Version WHERE MonthKey = @k", ("@k", key));
        if (version is null)
        {
            Exec(c, tx, "INSERT INTO dbo.Months (MonthKey) VALUES (@k)", ("@k", key));
            version = 1;
        }
        else
        {
            foreach (var t in ChildTables) Exec(c, tx, $"DELETE FROM dbo.{t} WHERE MonthKey = @k", ("@k", key));
        }
        ApplyInto(c, tx, key, doc);
        tx.Commit();
        return version.Value;
    }

    // ---------- document → rows ----------

    private static void ApplyInto(SqlConnection c, SqlTransaction tx, string key, JsonObject doc)
    {
        foreach (var (name, node) in doc)
        {
            if (node is not JsonObject obj) continue;
            switch (name)
            {
                case "settings":
                    foreach (var (field, value) in obj)
                        if (SettingColumns.TryGetValue(field, out var col))
                            Exec(c, tx, $"UPDATE dbo.Months SET {col} = @v WHERE MonthKey = @k", ("@v", ToDecimal(value)), ("@k", key));
                    break;

                case "days":
                    foreach (var (d, day) in obj)
                    {
                        if (!TryDay(d, out var dayNo)) continue;
                        (string, object)[] keys = [("MonthKey", key), ("Day", dayNo)];
                        if (day is null) Delete(c, tx, "DailyKharch", keys);
                        else if (day is JsonObject o) Upsert(c, tx, "DailyKharch", keys, DayFields, o);
                    }
                    break;

                case "counts":
                    foreach (var (d, day) in obj)
                    {
                        if (!TryDay(d, out var dayNo)) continue;
                        if (day is null) { Delete(c, tx, "MealCounts", [("MonthKey", key), ("Day", dayNo)]); continue; }
                        if (day is not JsonObject people) continue;
                        foreach (var (pid, counts) in people)
                        {
                            if (!IdRx().IsMatch(pid)) continue;
                            (string, object)[] keys = [("MonthKey", key), ("Day", dayNo), ("PersonId", pid)];
                            if (counts is null) Delete(c, tx, "MealCounts", keys);
                            else if (counts is JsonObject o) Upsert(c, tx, "MealCounts", keys, CountFields, o);
                        }
                    }
                    break;

                default:
                    var section = Sections.FirstOrDefault(s => s.Json == name);
                    if (section is null) break;
                    foreach (var (id, item) in obj)
                    {
                        if (!IdRx().IsMatch(id)) continue;
                        (string, object)[] keys = [("MonthKey", key), (section.IdColumn, id)];
                        if (item is null) Delete(c, tx, section.Table, keys);
                        else if (item is JsonObject o) Upsert(c, tx, section.Table, keys, section.Fields, o);
                    }
                    break;
            }
        }
    }

    /// <summary>Updates the row's given columns, inserting the row first if it doesn't exist.</summary>
    private static void Upsert(SqlConnection c, SqlTransaction tx, string table, (string Col, object Val)[] keys, Field[] fields, JsonObject data)
    {
        var sets = new List<(string Col, object Val)>();
        foreach (var f in fields)
        {
            if (!data.TryGetPropertyValue(f.Json, out var v)) continue;
            switch (f.Kind)
            {
                case Kind.Text:
                    sets.Add((f.Column, Clip(Str(v), f.Max)));
                    break;
                case Kind.Expr:
                    var text = Clip(Str(v), 200);
                    sets.Add((f.Column, text));
                    sets.Add((f.ValueColumn!, Expr.Eval(text)));
                    break;
                case Kind.Int:
                    sets.Add((f.Column, ToInt(v)));
                    break;
                case Kind.Date:
                    sets.Add((f.Column, ToDate(v)));
                    break;
            }
        }

        var where = string.Join(" AND ", keys.Select((k, i) => $"{k.Col} = @k{i}"));
        var cols = string.Join(", ", keys.Select(k => k.Col).Concat(sets.Select(s => s.Col)));
        var vals = string.Join(", ", keys.Select((_, i) => $"@k{i}").Concat(sets.Select((_, i) => $"@s{i}")));
        var insert = $"INSERT INTO dbo.{table} ({cols}) VALUES ({vals});";
        var sql = sets.Count > 0
            ? $"UPDATE dbo.{table} SET {string.Join(", ", sets.Select((s, i) => $"{s.Col} = @s{i}"))} WHERE {where}; IF @@ROWCOUNT = 0 {insert}"
            : $"IF NOT EXISTS (SELECT 1 FROM dbo.{table} WHERE {where}) {insert}";

        var args = keys.Select((k, i) => ($"@k{i}", k.Val)).Concat(sets.Select((s, i) => ($"@s{i}", s.Val))).ToArray();
        Exec(c, tx, sql, args);
    }

    private static void Delete(SqlConnection c, SqlTransaction tx, string table, (string Col, object Val)[] keys)
    {
        var where = string.Join(" AND ", keys.Select((k, i) => $"{k.Col} = @k{i}"));
        Exec(c, tx, $"DELETE FROM dbo.{table} WHERE {where}", keys.Select((k, i) => ($"@k{i}", k.Val)).ToArray());
    }

    // ---------- rows → document ----------

    private static List<string> MonthKeys(SqlConnection c)
    {
        using var cmd = new SqlCommand("SELECT MonthKey FROM dbo.Months ORDER BY MonthKey", c);
        using var r = cmd.ExecuteReader();
        var keys = new List<string>();
        while (r.Read()) keys.Add(r.GetString(0));
        return keys;
    }

    private static JsonObject? Read(SqlConnection c, string key)
    {
        var doc = new JsonObject { ["key"] = key };
        using (var cmd = Command(c, null, "SELECT CookMeals, CookTeas, BottlePrice, BorchiDaily, Version FROM dbo.Months WHERE MonthKey = @k", ("@k", key)))
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read()) return null;
            doc["settings"] = new JsonObject
            {
                ["cookMeals"] = (double)r.GetDecimal(0),
                ["cookTeas"] = (double)r.GetDecimal(1),
                ["bottlePrice"] = (double)r.GetDecimal(2),
                ["borchiDaily"] = (double)r.GetDecimal(3),
            };
            doc["version"] = r.GetInt32(4);
        }

        foreach (var s in Sections) doc[s.Json] = ReadRows(c, key, s.Table, s.IdColumn, s.Fields);
        doc["days"] = ReadRows(c, key, "DailyKharch", "Day", DayFields);

        var counts = new JsonObject();
        using (var cmd = Command(c, null, "SELECT Day, PersonId, Chai, Vella FROM dbo.MealCounts WHERE MonthKey = @k", ("@k", key)))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var day = r.GetByte(0).ToString(CultureInfo.InvariantCulture);
                if (counts[day] is not JsonObject people) counts[day] = people = new JsonObject();
                people[r.GetString(1)] = new JsonObject { ["c"] = r.GetInt32(2), ["m"] = r.GetInt32(3) };
            }
        }
        doc["counts"] = counts;
        return doc;
    }

    private static JsonObject ReadRows(SqlConnection c, string key, string table, string idColumn, Field[] fields)
    {
        var result = new JsonObject();
        var cols = string.Join(", ", fields.Select(f => f.Column));
        using var cmd = Command(c, null, $"SELECT {idColumn}, {cols} FROM dbo.{table} WHERE MonthKey = @k", ("@k", key));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = Convert.ToString(r.GetValue(0), CultureInfo.InvariantCulture)!;
            var item = new JsonObject();
            for (var i = 0; i < fields.Length; i++)
            {
                var f = fields[i];
                var v = r.GetValue(i + 1);
                item[f.Json] = f.Kind switch
                {
                    Kind.Int => JsonValue.Create(Convert.ToInt32(v)),
                    Kind.Date => v is DateTime d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "",
                    _ => JsonValue.Create(Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""),
                };
            }
            result[id] = item;
        }
        return result;
    }

    // ---------- helpers ----------

    private SqlConnection Open()
    {
        var c = new SqlConnection(connectionString);
        c.Open();
        return c;
    }

    private static SqlCommand Command(SqlConnection c, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand(sql, c, tx);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void Exec(SqlConnection c, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, tx, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static int? Scalar(SqlConnection c, SqlTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, tx, sql, args);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : Convert.ToInt32(v);
    }

    private static bool TryDay(string text, out byte day) => byte.TryParse(text, out day) && day is >= 1 and <= 31;

    private static string Str(JsonNode? v) => v switch
    {
        null => "",
        JsonValue jv when jv.GetValueKind() == JsonValueKind.String => jv.GetValue<string>(),
        JsonValue jv => jv.ToJsonString(),
        _ => "",
    };

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static int ToInt(JsonNode? v)
    {
        var d = v is JsonValue jv && jv.GetValueKind() == JsonValueKind.Number ? jv.GetValue<double>() : (double)Expr.Eval(Str(v));
        return (int)Math.Clamp(Math.Round(d), 0, 100_000);
    }

    private static decimal ToDecimal(JsonNode? v) =>
        v is JsonValue jv && jv.GetValueKind() == JsonValueKind.Number
            ? Math.Clamp(Math.Round(jv.GetValue<decimal>(), 2), -1_000_000_000m, 1_000_000_000m)
            : Expr.Eval(Str(v));

    private static object ToDate(JsonNode? v) =>
        DateTime.TryParseExact(Str(v), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DBNull.Value;
}
