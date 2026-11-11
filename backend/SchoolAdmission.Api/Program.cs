using Npgsql;
using System.Data;
using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;
using SchoolAdmission.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var connection = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=school_admission;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AdmissionDbContext>(o => o.UseNpgsql(connection));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseCors();
app.UseSwagger(); app.UseSwaggerUI();

// Database is persistent in the PostgreSQL volume. EnsureCreated keeps this take-home app runnable without a migration command.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    await db.Database.EnsureCreatedAsync();
    // EnsureCreated does not update an existing database, so keep the invariant
    // enforced even when the app is restarted with an already-created volume.
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_holds_student_one_active_application " +
        "ON \"Holds\" (\"StudentId\") WHERE \"Status\" IN ('HELD', 'CONFIRMED')");
}

int HoldSeconds() => Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS"), out var n) ? n : 600, 30, 86400);
string? Student(HttpRequest req) => req.Headers.TryGetValue("X-Student-Id", out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString().Trim() : null;

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/classes", async (CreateClassRequest input, AdmissionDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 160 || input.TotalSeats <= 0)
        return Results.BadRequest(new { message = "Enter a class name and specify a seat count of 1 or more." });
    var entity = new AdmissionClass { Name = input.Name.Trim(), TotalSeats = input.TotalSeats };
    db.Classes.Add(entity); await db.SaveChangesAsync();
    return Results.Created($"/classes/{entity.Id}", new { id = entity.Id.ToString(), name = entity.Name, totalSeats = entity.TotalSeats });
});

async Task<ClassResponse?> ClassDto(AdmissionDbContext db, AdmissionClass c)
{
    var now = DateTime.UtcNow;
    var holds = await db.Holds.Where(h => h.ClassId == c.Id).ToListAsync();
    foreach (var h in holds.Where(h => h.Status == "HELD" && h.ExpiresAt <= now)) h.Status = "EXPIRED";
    if (holds.Any(h => h.Status == "HELD" && h.ExpiresAt <= now)) await db.SaveChangesAsync();
    var held = holds.Count(h => h.Status == "HELD" && h.ExpiresAt > now);
    var confirmed = holds.Count(h => h.Status == "CONFIRMED");
    return new(c.Id.ToString(), c.Name, c.TotalSeats, held, confirmed, Math.Max(0, c.TotalSeats - held - confirmed));
}
app.MapGet("/classes", async (AdmissionDbContext db) =>
{
    var classes = await db.Classes.OrderBy(c => c.Name).ToListAsync();
    var result = new List<ClassResponse>(); foreach (var c in classes) result.Add((await ClassDto(db, c))!);
    return Results.Ok(result);
});
app.MapGet("/classes/{classId:guid}", async (Guid classId, AdmissionDbContext db) =>
{
    var c = await db.Classes.FindAsync(classId); if (c is null) return Results.NotFound(new { message = "Class not found." });
    return Results.Ok(await ClassDto(db, c));
});

app.MapPost("/classes/{classId:guid}/holds", async (Guid classId, HttpRequest request, AdmissionDbContext db) =>
{
    var studentId = Student(request);
    if (studentId is null || studentId.Length is < 5 or > 32 || !studentId.All(char.IsDigit))
        return Results.BadRequest(new { message = "Send the correct birth registration number in the X-Student-Id header." });
    // Student advisory lock + class row lock serialize cross-class student checks and seat allocation.
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    try
    {
        // Serialize applications by student first, even when requests target different classes.
        // This closes the race where one student could apply to two classes simultaneously.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({studentId}, 0))");

        var now = DateTime.UtcNow;
        var studentHolds = await db.Holds.Where(h => h.StudentId == studentId).ToListAsync();
        foreach (var h in studentHolds.Where(h => h.Status == "HELD" && h.ExpiresAt <= now)) h.Status = "EXPIRED";
        var existing = studentHolds.FirstOrDefault(h => h.Status == "HELD" && h.ExpiresAt > now)
            ?? studentHolds.FirstOrDefault(h => h.Status == "CONFIRMED");
        if (existing is not null)
        {
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return Results.Conflict(new { message = existing.Status == "CONFIRMED" ? "This student's admission is already confirmed." : "This student has an active application.", holdId = existing.Id });
        }

        // Lock the class row so concurrent students cannot take the same final seat.
        var c = await db.Classes.FromSqlInterpolated($"SELECT * FROM \"Classes\" WHERE \"Id\" = {classId} FOR UPDATE").FirstOrDefaultAsync();
        if (c is null) return Results.NotFound(new { message = "Class not found." });
        var occupied = await db.Holds.CountAsync(h => h.ClassId == classId && (h.Status == "CONFIRMED" || (h.Status == "HELD" && h.ExpiresAt > now)));
        if (occupied >= c.TotalSeats) return Results.Conflict(new { message = "Sorry, all seats in this class are full." });
        var hold = new AdmissionHold { ClassId = classId, StudentId = studentId, ExpiresAt = now.AddSeconds(HoldSeconds()) };
        db.Holds.Add(hold); await db.SaveChangesAsync(); await tx.CommitAsync();
        return Results.Created($"/holds/{hold.Id}", new HoldResponse(hold.Id.ToString(), classId.ToString(), studentId, hold.Status, hold.ExpiresAt));
    }
    catch (Exception ex) when (ex is DbUpdateException or NpgsqlException)
    {
        await tx.RollbackAsync(); return Results.Conflict(new { message = "Many applications have been received at the same time. Please try again." });
    }
});

app.MapGet("/holds/{holdId:guid}", async (Guid holdId, AdmissionDbContext db) =>
{
    var h = await db.Holds.FindAsync(holdId); if (h is null) return Results.NotFound(new { message = "Application not found." });
    if (h.Status == "HELD" && h.ExpiresAt <= DateTime.UtcNow) { h.Status = "EXPIRED"; await db.SaveChangesAsync(); }
    return Results.Ok(new HoldStatusResponse(h.Id.ToString(), h.ClassId.ToString(), h.Status, h.ExpiresAt));
});
app.MapPost("/holds/{holdId:guid}/confirm", async (Guid holdId, HttpRequest request, AdmissionDbContext db) =>
{
    var studentId = Student(request); if (studentId is null) return Results.BadRequest(new { message = "The X-Student-Id header is required." });
    if (studentId.Length is < 5 or > 32 || !studentId.All(char.IsDigit))
        return Results.BadRequest(new { message = "Provide the correct birth registration number." });
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var h = await db.Holds.FromSqlInterpolated($"SELECT * FROM \"Holds\" WHERE \"Id\" = {holdId} FOR UPDATE").FirstOrDefaultAsync();
    if (h is null) return Results.NotFound(new { message = "Application not found." });
    if (h.StudentId != studentId) return Results.Json(new { message = "This application is not yours." }, statusCode: 403);
    if (h.Status == "CONFIRMED") { await tx.CommitAsync(); return Results.Ok(new { holdId = h.Id.ToString(), status = h.Status }); }
    if (h.Status != "HELD" || h.ExpiresAt <= DateTime.UtcNow)
    {
        if (h.Status == "HELD") { h.Status = "EXPIRED"; await db.SaveChangesAsync(); }
        await tx.CommitAsync();
        return Results.Conflict(new { message = "The application period has ended. Please submit a new application.", status = h.Status });
    }
    h.Status = "CONFIRMED"; h.ConfirmedAt = DateTime.UtcNow; await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(new { holdId = h.Id.ToString(), status = h.Status });
});
app.MapDelete("/holds/{holdId:guid}", async (Guid holdId, HttpRequest request, AdmissionDbContext db) =>
{
    var studentId = Student(request); if (studentId is null || studentId.Length is < 5 or > 32 || !studentId.All(char.IsDigit)) return Results.BadRequest(new { message = "সঠিক জন্ম নিবন্ধন নম্বর দিন।" });
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
    var h = await db.Holds.FromSqlInterpolated($"SELECT * FROM \"Holds\" WHERE \"Id\" = {holdId} FOR UPDATE").FirstOrDefaultAsync();
    if (h is null) return Results.NotFound(new { message = "Application not found." });
    if (h.StudentId != studentId) return Results.Json(new { message = "This application is not yours." }, statusCode: 403);
    if (h.Status == "CONFIRMED") return Results.Conflict(new { message = "A confirmed admission cannot be cancelled." });
    if (h.Status == "HELD") { h.Status = "CANCELLED"; await db.SaveChangesAsync(); }
    await tx.CommitAsync();
    return Results.NoContent();
});
app.MapGet("/me/holds", async (HttpRequest request, AdmissionDbContext db) =>
{
    var studentId = Student(request); if (studentId is null) return Results.BadRequest(new { message = "Provide your birth registration number." });
    var now = DateTime.UtcNow;
    var items = await db.Holds.Include(h => h.Class).Where(h => h.StudentId == studentId).OrderByDescending(h => h.CreatedAt).ToListAsync();
    foreach (var h in items.Where(h => h.Status == "HELD" && h.ExpiresAt <= now)) h.Status = "EXPIRED";
    await db.SaveChangesAsync();
    return Results.Ok(items.Select(h => new { holdId = h.Id.ToString(), classId = h.ClassId.ToString(), className = h.Class!.Name, status = h.Status, expiresAt = h.ExpiresAt }));
});
app.Run();
