using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlatKharchKhata;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Khata")
    ?? throw new InvalidOperationException("Add ConnectionStrings:Khata to appsettings.json.");
// Leave App:Password empty to use the site without a login (fine on your own PC).
// Set it before putting the site online; everyone signs in with that one password.
var password = builder.Configuration["App:Password"] ?? "";
var loginRequired = password.Length > 0;

builder.Services.AddSingleton(new KhataStore(connectionString));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "khata";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromDays(60);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.Services.GetRequiredService<KhataStore>().EnsureDatabase(Path.Combine(AppContext.BaseDirectory, "Data"));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ---------- login ----------

app.MapGet("/api/session", (HttpContext http) => new
{
    loginRequired,
    signedIn = !loginRequired || http.User.Identity?.IsAuthenticated == true,
});

app.MapPost("/api/login", async (HttpContext http, LoginRequest req) =>
{
    if (!loginRequired) return Results.Ok();
    var ok = CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(req.Password ?? "")),
        SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    if (!ok)
    {
        await Task.Delay(700);
        return Results.Unauthorized();
    }
    var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "flatmate")], CookieAuthenticationDefaults.AuthenticationScheme);
    await http.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
    return Results.Ok();
});

app.MapPost("/api/logout", async (HttpContext http) =>
{
    await http.SignOutAsync();
    return Results.Ok();
});

// ---------- months ----------

var months = app.MapGroup("/api/months");
if (loginRequired) months.RequireAuthorization();

months.MapGet("/", (KhataStore store) => Results.Json(store.GetAll()));

months.MapGet("/versions", (KhataStore store) => Results.Json(store.Versions()));

months.MapGet("/{key}", (string key, KhataStore store) =>
    KhataStore.ValidMonth(key) && store.Get(key) is { } doc ? Results.Json(doc) : Results.NotFound());

months.MapPost("/", (JsonObject doc, KhataStore store) =>
{
    var key = doc["key"]?.GetValue<string>();
    if (!KhataStore.ValidMonth(key)) return Results.BadRequest("Month must look like 2026-10.");
    return store.Create(key!, doc) ? Results.Ok(new { version = 1 }) : Results.Conflict("That month already exists.");
});

months.MapPost("/{key}/patch", (string key, JsonObject patch, KhataStore store) =>
{
    if (!KhataStore.ValidMonth(key)) return Results.BadRequest("Month must look like 2026-10.");
    return store.Patch(key, patch) is { } version ? Results.Ok(new { version }) : Results.NotFound();
});

months.MapPut("/{key}", (string key, JsonObject doc, KhataStore store) =>
{
    if (!KhataStore.ValidMonth(key)) return Results.BadRequest("Month must look like 2026-10.");
    return Results.Ok(new { version = store.Replace(key, doc) });
});

app.Run();

internal sealed record LoginRequest(string? Password);
