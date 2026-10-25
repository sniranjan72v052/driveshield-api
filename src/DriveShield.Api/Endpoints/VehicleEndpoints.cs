using System.Text.RegularExpressions;
using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

/// <summary>A person's own vehicles. Choosing one when reporting a problem tells providers what they are coming to.</summary>
public static partial class VehicleEndpoints
{
    private const int MaxVehicles = 10;

    public static void MapVehicleEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/vehicles").RequireAuthorization();
        g.MapGet("/", List);
        g.MapPost("/", Add);
        g.MapPut("/{id:guid}", Update);
        g.MapDelete("/{id:guid}", Delete);
    }

    public static VehicleDto ToDto(Vehicle v) =>
        new(v.Id, v.Make, v.Model, v.RegistrationNumber, v.IsElectric, v.InsurerName, v.PolicyNumber);

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9 \-]{2,13}[A-Z0-9]$")]
    private static partial Regex RegistrationShape();

    /// <summary>Cleans the typed values. Returns an error message, or null when everything is acceptable.</summary>
    private static string? Apply(VehicleRequest req, Vehicle v)
    {
        var make = (req.Make ?? "").Trim();
        var model = (req.Model ?? "").Trim();
        var reg = Regex.Replace((req.RegistrationNumber ?? "").Trim().ToUpperInvariant(), @"\s+", " ");
        var insurer = (req.InsurerName ?? "").Trim();
        var policy = (req.PolicyNumber ?? "").Trim();

        if (make.Length == 0 || make.Length > 60) return "Enter the make, for example Maruti or Tata (up to 60 characters).";
        if (model.Length == 0 || model.Length > 60) return "Enter the model, for example Swift or Nexon (up to 60 characters).";
        if (!RegistrationShape().IsMatch(reg)) return "Enter the registration number, for example MH12 AB 1234.";
        if (insurer.Length > 100) return "The insurer's name is too long (up to 100 characters).";
        if (policy.Length > 50) return "The policy number is too long (up to 50 characters).";

        v.Make = make;
        v.Model = model;
        v.RegistrationNumber = reg;
        v.IsElectric = req.IsElectric ?? false;
        v.InsurerName = insurer.Length == 0 ? null : insurer;
        v.PolicyNumber = policy.Length == 0 ? null : policy;
        return null;
    }

    private static readonly IResult CustomersOnly = Results.Json(
        new { error = "Saved vehicles are for customers." }, statusCode: StatusCodes.Status403Forbidden);

    private static async Task<IResult> List(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        if (user.Role != UserRole.Customer) return Results.Ok(new List<VehicleDto>());
        var rows = await db.Vehicles.AsNoTracking()
            .Where(v => v.OwnerId == user.Id)
            .OrderBy(v => v.CreatedAt)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(ToDto).ToList());
    }

    private static async Task<IResult> Add(
        VehicleRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        if (user.Role != UserRole.Customer) return CustomersOnly;
        var vehicle = new Vehicle { OwnerId = user.Id };
        if (Apply(req, vehicle) is { } problem) return Results.BadRequest(new { error = problem });

        if (await db.Vehicles.CountAsync(v => v.OwnerId == user.Id, ct) >= MaxVehicles)
            return Results.BadRequest(new { error = $"You can save up to {MaxVehicles} vehicles. Remove one first." });
        if (await db.Vehicles.AnyAsync(v => v.OwnerId == user.Id && v.RegistrationNumber == vehicle.RegistrationNumber, ct))
            return Results.Conflict(new { error = "You have already saved a vehicle with that registration number." });

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/vehicles/{vehicle.Id}", ToDto(vehicle));
    }

    private static async Task<IResult> Update(
        Guid id, VehicleRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        if (user.Role != UserRole.Customer) return CustomersOnly;
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == user.Id, ct);
        if (vehicle is null) return Results.NotFound();
        if (Apply(req, vehicle) is { } problem) return Results.BadRequest(new { error = problem });

        if (await db.Vehicles.AnyAsync(v => v.OwnerId == user.Id && v.Id != id && v.RegistrationNumber == vehicle.RegistrationNumber, ct))
            return Results.Conflict(new { error = "You have already saved a vehicle with that registration number." });

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(vehicle));
    }

    /// <summary>Past requests keep working: they simply no longer show a vehicle.</summary>
    private static async Task<IResult> Delete(Guid id, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        if (user.Role != UserRole.Customer) return CustomersOnly;
        var removed = await db.Vehicles.Where(v => v.Id == id && v.OwnerId == user.Id).ExecuteDeleteAsync(ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }
}
