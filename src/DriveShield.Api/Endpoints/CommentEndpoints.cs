using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Hubs;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

/// <summary>
/// Photos and comments on each step of a case.
///   POST   /api/cases/{id}/comments               multipart form: step, body, photos (repeat the field for several files)
///   PATCH  /api/cases/{id}/comments/{commentId}   change the text of your own post
///   DELETE /api/cases/{id}/comments/{commentId}   delete a post and its photos (author, or admin with ?reason=)
///   DELETE /api/cases/{id}/photos/{photoId}       delete one photo (author, or admin with ?reason=)
///   GET    /api/cases/{id}/photos/{photoId}       the image file (needs sign-in, so the apps fetch it with their auth header)
///
/// Who may post and see them: the customer, a provider who has accepted the case, and admins.
/// Who may change them: the author while the case is still open (not Reviewed or Cancelled), and admins at any time.
/// An admin must give a reason, which is stored with the removal. A removed post stays in the thread as a placeholder;
/// its text and photo files are deleted.
/// </summary>
public static class CommentEndpoints
{
    private const int MaxPhotosPerPost = 5;
    private const int MaxPhotoBytes = 5 * 1024 * 1024; // the apps shrink photos before upload, so real files are far smaller
    private const int MaxPhotosPerCase = 60;
    /// <summary>At the request stage (the Reported step) a customer may attach at most this many photos in total.</summary>
    private const int MaxCustomerPhotosAtRequest = 2;
    private const int MaxBodyLength = 1000;
    private const int MaxReasonLength = 300;

    // The stops of the progress route. Cancelled and Escalated are not stops.
    private static readonly CaseStatus[] RouteSteps =
    [
        CaseStatus.Reported, CaseStatus.Assessed, CaseStatus.ProviderMatched, CaseStatus.ProviderAccepted,
        CaseStatus.EnRoute, CaseStatus.InProgress, CaseStatus.Resolved, CaseStatus.Reviewed
    ];

    public static void MapCommentEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cases").RequireAuthorization();

        // The API uses header tokens, not cookies, so cross-site request forgery does not apply to these routes.
        g.MapPost("/{caseId:guid}/comments", AddComment).DisableAntiforgery();
        g.MapPatch("/{caseId:guid}/comments/{commentId:guid}", EditComment);
        g.MapDelete("/{caseId:guid}/comments/{commentId:guid}", DeleteComment);
        g.MapGet("/{caseId:guid}/photos/{photoId:guid}", GetPhoto);
        g.MapDelete("/{caseId:guid}/photos/{photoId:guid}", RemovePhoto);
    }

    /// <summary>Authors can change their own posts only while the case is still going. After that, only an admin can.</summary>
    private static bool IsCaseOpen(CaseStatus status) => status is not (CaseStatus.Reviewed or CaseStatus.Cancelled);

    // ------------------------------------------------------------------ read (used by GET /api/cases/{id})

    public static async Task<List<CommentDto>> LoadCommentsAsync(
        AppDbContext db, Guid caseId, Guid viewerUserId, bool viewerIsAdmin, CaseStatus caseStatus, CancellationToken ct)
    {
        var rows = await db.CaseComments.AsNoTracking()
            .Include(c => c.Photos)
            .Where(c => c.CaseId == caseId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var providerUserIds = rows.Where(r => r.Actor == Actor.Provider).Select(r => r.AuthorUserId).Distinct().ToList();
        var businessNames = await db.ProviderProfiles.AsNoTracking()
            .Where(p => providerUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.BusinessName, ct);

        var open = IsCaseOpen(caseStatus);
        return rows
            .Select(r => ToDto(r, LabelFor(r.Actor, businessNames.GetValueOrDefault(r.AuthorUserId)), viewerUserId, viewerIsAdmin, open))
            .ToList();
    }

    private static string LabelFor(Actor actor, string? businessName) => actor switch
    {
        Actor.Customer => "Customer",
        Actor.Admin => "Support team",
        _ => string.IsNullOrWhiteSpace(businessName) ? "Provider" : businessName
    };

    private static async Task<string> AuthorLabelAsync(AppDbContext db, Guid userId, Actor actor, CancellationToken ct)
    {
        if (actor != Actor.Provider) return LabelFor(actor, null);
        var name = await db.ProviderProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.BusinessName)
            .FirstOrDefaultAsync(ct);
        return LabelFor(actor, name);
    }

    private static string RemovedLabel(Actor? by) => by == Actor.Admin ? "Support" : "Author";

    /// <summary>Builds what one viewer sees of one post, including what they are allowed to do with it.</summary>
    private static CommentDto ToDto(CaseComment c, string label, Guid viewerUserId, bool viewerIsAdmin, bool caseOpen)
    {
        var mine = c.AuthorUserId == viewerUserId;

        if (c.DeletedAt is not null)
        {
            return new CommentDto(
                c.Id, c.Step.ToString(), c.Actor.ToString(), label, null, c.CreatedAt, [], c.EditedAt,
                RemovedLabel(c.DeletedByActor), viewerIsAdmin ? c.RemovalReason : null, mine, false, false);
        }

        var authorMayChange = mine && caseOpen;
        var photos = c.Photos
            .OrderBy(p => p.CreatedAt)
            .Select(p => p.RemovedAt is not null
                ? new PhotoDto(p.Id, p.ContentType, RemovedLabel(p.RemovedByActor), viewerIsAdmin ? p.RemovalReason : null, false)
                : new PhotoDto(p.Id, p.ContentType, null, null, authorMayChange || viewerIsAdmin))
            .ToList();

        return new CommentDto(
            c.Id, c.Step.ToString(), c.Actor.ToString(), label, c.Body, c.CreatedAt, photos, c.EditedAt,
            null, null, mine, authorMayChange, authorMayChange || viewerIsAdmin);
    }

    // ------------------------------------------------------------------ shared helpers

    private sealed record CaseContext(AppUser User, Case Case, CaseAccess Access, Actor Actor);

    /// <summary>Loads the case and what the caller may do on it. Null means "not found", also for people with no access.</summary>
    private static async Task<CaseContext?> LoadContextAsync(
        Guid caseId, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return null;

        var access = await CaseAccess.GetAsync(db, user, c, ct);
        return access.ActingAs is { } actor ? new CaseContext(user, c, access, actor) : null;
    }

    private static string? CleanReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var clean = new string(raw.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length == 0) return null;
        return clean.Length > MaxReasonLength ? clean[..MaxReasonLength] : clean;
    }

    /// <summary>
    /// Who may remove a post or photo: its author while the case is open, or an admin at any time.
    /// An admin removing someone else's content must give a reason. Returns an error result, or null when allowed.
    /// </summary>
    private static IResult? CheckMayRemove(CaseContext ctx, Guid authorUserId, string? reason, out Actor removedBy, out string? cleanReason)
    {
        var byAuthor = authorUserId == ctx.User.Id;
        var isAdmin = ctx.Access.IsAdmin;
        cleanReason = null;
        removedBy = byAuthor ? ctx.Actor : Actor.Admin;

        if (!byAuthor && !isAdmin) return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (byAuthor)
        {
            if (!isAdmin && !IsCaseOpen(ctx.Case.Status))
                return Results.Conflict(new { error = "This case is closed, so posts can no longer be changed. Contact support if something needs to be removed." });
            return null;
        }

        cleanReason = CleanReason(reason);
        return cleanReason is null ? Results.BadRequest(new { error = "Say why you are removing this." }) : null;
    }

    private static void MarkPhotoRemoved(CasePhoto p, Guid userId, Actor by, string? reason, DateTimeOffset now)
    {
        p.RemovedAt = now;
        p.RemovedByUserId = userId;
        p.RemovedByActor = by;
        p.RemovalReason = reason;
    }

    /// <summary>Marks a whole post as removed, clears its text and removes its photos. Returns the files to delete once saved.</summary>
    private static List<string> MarkPostRemoved(CaseComment comment, Guid userId, Actor by, string? reason, DateTimeOffset now)
    {
        var keys = new List<string>();
        foreach (var p in comment.Photos.Where(p => p.RemovedAt is null))
        {
            MarkPhotoRemoved(p, userId, by, reason, now);
            keys.Add(p.StorageKey);
        }

        comment.Body = null;
        comment.DeletedAt = now;
        comment.DeletedByUserId = userId;
        comment.DeletedByActor = by;
        comment.RemovalReason = reason;
        return keys;
    }

    /// <summary>Files go after the database save, so a failed save never leaves a post pointing at a missing file.</summary>
    private static async Task DeleteFilesAsync(IPhotoStore store, IEnumerable<string> keys, ILogger log)
    {
        foreach (var key in keys)
        {
            try
            {
                await store.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not delete photo file {Key}; it is no longer reachable through the API.", key);
            }
        }
    }

    private static Task NotifyChangedAsync(IHubContext<CaseHub> hub, Guid caseId, CaseStatus step, CancellationToken ct) =>
        hub.Clients.Group(CaseHub.CaseGroup(caseId)).SendAsync("CommentChanged", new CommentAddedEvent(caseId, step.ToString()), ct);

    // ------------------------------------------------------------------ add

    private static async Task<IResult> AddComment(
        Guid caseId, HttpContext http, UserResolver users, AppDbContext db, IPhotoStore store,
        IHubContext<CaseHub> hub, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Send the comment as a form with an optional photos field." });

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return Results.BadRequest(new { error = "The upload is too large. Try fewer or smaller photos." });
        }

        var body = form["body"].ToString().Trim();
        var files = form.Files.GetFiles("photos");

        if (body.Length == 0 && files.Count == 0)
            return Results.BadRequest(new { error = "Add a photo or write a comment." });
        if (body.Length > MaxBodyLength)
            return Results.BadRequest(new { error = $"The comment is too long (max {MaxBodyLength} characters)." });
        if (files.Count > MaxPhotosPerPost)
            return Results.BadRequest(new { error = $"You can add up to {MaxPhotosPerPost} photos at a time." });

        var ctx = await LoadContextAsync(caseId, http, users, db, ct);
        if (ctx is null) return Results.NotFound(); // same answer as "missing": no probing
        var user = ctx.User;
        var actor = ctx.Actor;

        // Which step? Default to the latest route step the case has reached.
        var reached = await db.CaseStatusHistory.AsNoTracking()
            .Where(h => h.CaseId == caseId)
            .Select(h => h.Status)
            .Distinct()
            .ToListAsync(ct);

        CaseStatus step;
        var rawStep = form["step"].ToString();
        if (string.IsNullOrWhiteSpace(rawStep))
        {
            step = reached.Where(s => RouteSteps.Contains(s)).OrderBy(s => Array.IndexOf(RouteSteps, s)).LastOrDefault();
        }
        else if (!Enum.TryParse(rawStep, out step) || !RouteSteps.Contains(step) || !reached.Contains(step))
        {
            return Results.BadRequest(new { error = "You can only add to a step this case has reached." });
        }

        if (files.Count > 0 && step == CaseStatus.Reported && actor == Actor.Customer)
        {
            var atRequest = await db.CasePhotos.CountAsync(p =>
                p.CaseId == caseId && p.RemovedAt == null && p.UploadedByUserId == user.Id &&
                db.CaseComments.Any(c => c.Id == p.CommentId && c.Step == CaseStatus.Reported), ct);
            if (atRequest + files.Count > MaxCustomerPhotosAtRequest)
                return Results.BadRequest(new { error = $"You can attach up to {MaxCustomerPhotosAtRequest} photos when reporting the problem." });
        }

        if (files.Count > 0)
        {
            var existing = await db.CasePhotos.CountAsync(p => p.CaseId == caseId && p.RemovedAt == null, ct);
            if (existing + files.Count > MaxPhotosPerCase)
                return Results.BadRequest(new { error = "This case has reached its photo limit." });
        }

        // Check every file before saving any of them. The type comes from the file's own bytes, not its name or header.
        var validated = new List<(IFormFile File, string Type)>();
        foreach (var file in files)
        {
            if (file.Length == 0 || file.Length > MaxPhotoBytes)
                return Results.BadRequest(new { error = $"\"{SafeName(file.FileName)}\" is empty or larger than {MaxPhotoBytes / 1024 / 1024} MB." });

            var header = new byte[12];
            int read;
            await using (var s = file.OpenReadStream())
            {
                read = await s.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            }

            var type = DetectImageType(header.AsSpan(0, read));
            if (type is null)
                return Results.BadRequest(new { error = $"\"{SafeName(file.FileName)}\" is not a JPEG, PNG or WebP photo." });
            validated.Add((file, type));
        }

        var comment = new CaseComment
        {
            CaseId = caseId,
            Step = step,
            AuthorUserId = user.Id,
            Actor = actor,
            Body = body.Length == 0 ? null : body
        };

        var savedKeys = new List<string>();
        try
        {
            foreach (var (file, type) in validated)
            {
                var photo = new CasePhoto
                {
                    CaseId = caseId,
                    CommentId = comment.Id,
                    UploadedByUserId = user.Id,
                    ContentType = type,
                    SizeBytes = (int)file.Length
                };
                photo.StorageKey = $"{caseId:N}/{photo.Id:N}";

                await using var stream = file.OpenReadStream();
                await store.SaveAsync(photo.StorageKey, stream, ct);
                savedKeys.Add(photo.StorageKey);
                comment.Photos.Add(photo);
            }

            db.CaseComments.Add(comment);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Do not leave orphan files behind if the database save failed.
            foreach (var key in savedKeys) await store.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        // Tell everyone watching this case, so the new photo shows up without a refresh.
        await hub.Clients.Group(CaseHub.CaseGroup(caseId)).SendAsync(
            "CommentAdded", new CommentAddedEvent(caseId, step.ToString()), ct);

        var label = await AuthorLabelAsync(db, user.Id, actor, ct);
        return Results.Created($"/api/cases/{caseId}", ToDto(comment, label, user.Id, ctx.Access.IsAdmin, IsCaseOpen(ctx.Case.Status)));
    }

    // ------------------------------------------------------------------ edit the text of your own post

    private static async Task<IResult> EditComment(
        Guid caseId, Guid commentId, EditCommentRequest req, HttpContext http, UserResolver users, AppDbContext db,
        IHubContext<CaseHub> hub, CancellationToken ct)
    {
        var body = req.Body?.Trim() ?? "";
        if (body.Length > MaxBodyLength)
            return Results.BadRequest(new { error = $"The comment is too long (max {MaxBodyLength} characters)." });

        var ctx = await LoadContextAsync(caseId, http, users, db, ct);
        if (ctx is null) return Results.NotFound();

        var comment = await db.CaseComments.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == commentId && x.CaseId == caseId, ct);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();

        // Only the author edits their own words, even admins: an admin can remove a post but never rewrite it.
        if (comment.AuthorUserId != ctx.User.Id) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!IsCaseOpen(ctx.Case.Status))
            return Results.Conflict(new { error = "This case is closed, so posts can no longer be changed. Contact support if something needs to be removed." });

        var hasPhotos = comment.Photos.Any(p => p.RemovedAt is null);
        if (body.Length == 0 && !hasPhotos)
            return Results.BadRequest(new { error = "Write a comment, or delete the post instead." });

        if (body != (comment.Body ?? ""))
        {
            comment.Body = body.Length == 0 ? null : body;
            comment.EditedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await NotifyChangedAsync(hub, caseId, comment.Step, ct);
        }

        var label = await AuthorLabelAsync(db, comment.AuthorUserId, comment.Actor, ct);
        return Results.Ok(ToDto(comment, label, ctx.User.Id, ctx.Access.IsAdmin, IsCaseOpen(ctx.Case.Status)));
    }

    // ------------------------------------------------------------------ delete a post

    private static async Task<IResult> DeleteComment(
        Guid caseId, Guid commentId, string? reason, HttpContext http, UserResolver users, AppDbContext db,
        IPhotoStore store, IHubContext<CaseHub> hub, ILoggerFactory loggers, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(caseId, http, users, db, ct);
        if (ctx is null) return Results.NotFound();

        var comment = await db.CaseComments.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == commentId && x.CaseId == caseId, ct);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();

        var denied = CheckMayRemove(ctx, comment.AuthorUserId, reason, out var removedBy, out var cleanReason);
        if (denied is not null) return denied;

        var keys = MarkPostRemoved(comment, ctx.User.Id, removedBy, cleanReason, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        await DeleteFilesAsync(store, keys, loggers.CreateLogger("DriveShield.Photos"));
        await NotifyChangedAsync(hub, caseId, comment.Step, ct);
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ delete one photo

    private static async Task<IResult> RemovePhoto(
        Guid caseId, Guid photoId, string? reason, HttpContext http, UserResolver users, AppDbContext db,
        IPhotoStore store, IHubContext<CaseHub> hub, ILoggerFactory loggers, CancellationToken ct)
    {
        var ctx = await LoadContextAsync(caseId, http, users, db, ct);
        if (ctx is null) return Results.NotFound();

        var photo = await db.CasePhotos.FirstOrDefaultAsync(p => p.Id == photoId && p.CaseId == caseId, ct);
        if (photo is null || photo.RemovedAt is not null) return Results.NotFound();

        var comment = await db.CaseComments.Include(x => x.Photos)
            .FirstOrDefaultAsync(x => x.Id == photo.CommentId && x.CaseId == caseId, ct);
        if (comment is null || comment.DeletedAt is not null) return Results.NotFound();

        var denied = CheckMayRemove(ctx, comment.AuthorUserId, reason, out var removedBy, out var cleanReason);
        if (denied is not null) return denied;

        var now = DateTimeOffset.UtcNow;
        var keys = new List<string> { photo.StorageKey };

        // `photo` and the matching item in comment.Photos are the same tracked object.
        MarkPhotoRemoved(photo, ctx.User.Id, removedBy, cleanReason, now);

        // A post with no words and no photos left has nothing to show: retire it too.
        var hasWords = !string.IsNullOrWhiteSpace(comment.Body);
        var hasPhotos = comment.Photos.Any(p => p.RemovedAt is null);
        if (!hasWords && !hasPhotos) keys.AddRange(MarkPostRemoved(comment, ctx.User.Id, removedBy, cleanReason, now));

        await db.SaveChangesAsync(ct);
        await DeleteFilesAsync(store, keys, loggers.CreateLogger("DriveShield.Photos"));
        await NotifyChangedAsync(hub, caseId, comment.Step, ct);
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ serve a photo

    private static async Task<IResult> GetPhoto(
        Guid caseId, Guid photoId, HttpContext http, UserResolver users, AppDbContext db, IPhotoStore store,
        CancellationToken ct)
    {
        var ctx = await LoadContextAsync(caseId, http, users, db, ct);
        if (ctx is null) return Results.NotFound();

        var photo = await db.CasePhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == photoId && p.CaseId == caseId && p.RemovedAt == null, ct);
        if (photo is null) return Results.NotFound();

        var stream = await store.OpenReadAsync(photo.StorageKey, ct);
        if (stream is null) return Results.NotFound();

        http.Response.Headers.CacheControl = "private, max-age=3600";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(stream, photo.ContentType);
    }

    // ------------------------------------------------------------------ helpers

    private static string? DetectImageType(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "image/jpeg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
                          && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return "image/png";
        if (h.Length >= 12 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                           && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P') return "image/webp";
        return null;
    }

    /// <summary>File names come from the user, so only show a trimmed, printable version in error messages.</summary>
    private static string SafeName(string name)
    {
        var clean = new string(name.Where(ch => !char.IsControl(ch)).ToArray());
        return clean.Length > 60 ? clean[..60] : clean;
    }
}
