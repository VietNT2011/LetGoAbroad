using EnglishHub.Application.Learning;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed partial class LearningService
{
    private sealed record PathState(PathVersion Version, PathNode[] Nodes, HashSet<Guid> Completed, int StudyDay, Dictionary<Guid, DailyTask> Missed);

    private static DateOnly LocalDate(DateTimeOffset instant, string zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);

    private void PinPath(Enrollment enrollment, PathVersion version, AppUser user)
    {
        enrollment.PathVersionId = version.Id; enrollment.Level = version.Template.Level;
        enrollment.PathStartedOn = LocalDate(clock.UtcNow, user.TimeZoneId);
        enrollment.PathTimeZoneId = user.TimeZoneId; enrollment.PathStudyDays = user.StudyDays;
    }

    /// <summary>VI: Người dùng cũ chọn mẫu rõ ràng; giữ task đang có của hôm nay. EN: Legacy learners explicitly adopt a published template while retaining their existing daily assignments.</summary>
    public Task<Guid> AdoptPath(Guid userId, AdoptPathInput input) => tx.Run($"learning:{userId}", async () =>
    {
        var hash = Receipts.Hash(input); var duplicate = await receipts.Find(userId, "adopt-path", input.ClientRequestId, hash);
        if (duplicate is not null) return duplicate.ResourceId;
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.UserId == userId) ?? throw new DomainError(409, "enrollment_required", "Hãy tạo enrollment trước.");
        DomainError.Require(enrollment.PathVersionId is null, 409, "path_already_pinned", "Enrollment đã ghim một version; không tự chuyển sang mẫu mới.");
        var version = await db.PathVersions.Include(x => x.Template).SingleOrDefaultAsync(x => x.Id == input.PathVersionId && x.Status == PublicationStatus.Published) ?? throw new DomainError(422, "path_unavailable", "Mẫu chưa được publish.");
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        PinPath(enrollment, version, user); enrollment.PlanRevision++; enrollment.SelectionReason = "legacy_adoption";
        receipts.Save(userId, "adopt-path", input.ClientRequestId, hash, version.Id, new { version.Id }); return version.Id;
    });

    public Task<object> PathPlan(Guid userId) => tx.Run($"learning:{userId}", async () =>
    {
        var enrollment = await db.Enrollments.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId);
        if (enrollment?.PathVersionId is null) return (object)new { template = (object?)null, nodes = Array.Empty<object>(), studyDay = 0, legacy = enrollment is not null };
        await SyncCompleted(userId, LocalDate(clock.UtcNow, enrollment.TimeZoneId), enrollment.TimeZoneId);
        var state = await ReadPathState(enrollment);
        return (object)new
        {
            template = new { state.Version.Id, state.Version.TemplateId, state.Version.Template.Slug, state.Version.Template.Level, state.Version.Version, state.Version.Title, state.Version.Summary, state.Version.Attribution, enrollment.PathStartedOn, enrollment.PathTimeZoneId, enrollment.PathStudyDays },
            state.StudyDay,
            legacy = false,
            nodes = state.Nodes.Select(node => new
            {
                node.Id,
                node.Key,
                node.ContentVersionId,
                node.ContentVersion.Title,
                node.ContentVersion.Summary,
                node.ContentVersion.Skill,
                kind = node.ContentVersion.Root.Kind,
                node.ContentVersion.Minutes,
                node.StudyDay,
                node.CompletionRule,
                node.MinimumCorrectPercent,
                stage = node.Stage.Title,
                completed = state.Completed.Contains(node.Id),
                unlocked = node.Prerequisites.All(p => state.Completed.Contains(p.PrerequisiteNodeId)),
                due = node.StudyDay <= state.StudyDay,
                prerequisites = node.Prerequisites.Select(p => p.PrerequisiteNodeId).ToArray()
            }).ToArray()
        };
    });

    private async Task<PathState> ReadPathState(Enrollment enrollment)
    {
        var version = await paths.Full.AsNoTracking().SingleAsync(x => x.Id == enrollment.PathVersionId);
        var nodes = version.Stages.OrderBy(x => x.Order).SelectMany(s => s.Nodes.OrderBy(n => n.Order)).ToArray();
        var learned = await db.LessonProgress.Where(x => x.UserId == enrollment.UserId).Select(x => x.ContentVersionId).ToArrayAsync();
        var completed = (await db.TaskCompletions.Where(x => x.DailyTask.EnrollmentId == enrollment.Id && x.DailyTask.PathNodeId != null).Select(x => x.DailyTask.PathNodeId!.Value).ToArrayAsync()).ToHashSet();
        completed.UnionWith(nodes.Where(n => n.CompletionRule == "lesson_quiz" && learned.Contains(n.ContentVersionId)).Select(n => n.Id));
        var missed = (await db.DailyTasks.AsNoTracking().Where(x => x.EnrollmentId == enrollment.Id && x.PathNodeId != null && x.Status == "missed").OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync()).GroupBy(x => x.PathNodeId!.Value).ToDictionary(g => g.Key, g => g.First());
        var day = PathScheduler.StudyDay(enrollment.PathStartedOn!.Value, LocalDate(clock.UtcNow, enrollment.PathTimeZoneId!), enrollment.PathStudyDays!);
        return new(version, nodes, completed, day, missed);
    }

    private async Task GeneratePathTasks(Enrollment enrollment, AppUser user, DateOnly today)
    {
        var state = await ReadPathState(enrollment);
        var pending = await db.DailyTasks.Where(x => x.EnrollmentId == enrollment.Id && x.PathNodeId != null && x.Status == "pending").Select(x => x.PathNodeId!.Value).ToArrayAsync();
        var candidates = state.Nodes.Select((node, order) => (node, order)).Where(x => !state.Completed.Contains(x.node.Id) && !pending.Contains(x.node.Id) && x.node.StudyDay <= state.StudyDay && x.node.Prerequisites.All(p => state.Completed.Contains(p.PrerequisiteNodeId)))
            .Select(x => new PathCandidate(x.node.Id, x.node.ContentVersionId, x.node.ContentVersion.Root.Kind == ContentKind.Lesson ? "lesson" : "assessment", x.node.ContentVersion.Minutes, x.node.StudyDay, x.order, state.Missed.GetValueOrDefault(x.node.Id)?.Id));
        var selected = PathScheduler.Select(candidates, user.MinutesPerDay);
        foreach (var (item, slot) in selected.Select((item, slot) => (item, slot)))
        {
            var node = state.Nodes.Single(x => x.Id == item.NodeId);
            db.DailyTasks.Add(new DailyTask
            {
                EnrollmentId = enrollment.Id,
                UserId = user.Id,
                LocalDate = today,
                SlotKey = slot,
                ContentVersionId = node.ContentVersionId,
                PathVersionId = state.Version.Id,
                PathNodeId = node.Id,
                SourceTaskId = item.SourceTaskId,
                EstimatedMinutes = item.Minutes,
                TimeZoneId = user.TimeZoneId,
                CreatedAt = clock.UtcNow,
                ReasonCode = item.SourceTaskId is not null ? "PATH_CARRYOVER" : item.Kind == "lesson" ? "PREREQUISITE_READY" : "PATH_PRACTICE",
                ReasonText = item.SourceTaskId is not null ? $"Tiếp tục nhiệm vụ từ {state.Missed[node.Id].LocalDate:dd/MM}; giữ nội dung và version lộ trình." : $"{node.Stage.Title} · ngày học gợi ý {node.StudyDay}, lộ trình v{state.Version.Version}; đã đủ điều kiện tiên quyết."
            });
        }
    }

    private async Task MarkMissed(Guid user, string fallbackZone)
    {
        var pending = await db.DailyTasks.Where(x => x.UserId == user && x.Status == "pending").ToListAsync();
        foreach (var task in pending)
        {
            if (task.LocalDate >= LocalDate(clock.UtcNow, task.TimeZoneId ?? fallbackZone)) continue;
            var active = await db.Attempts.AnyAsync(a => a.UserId == user && a.ContentVersionId == task.ContentVersionId && a.Status == AttemptStatus.InProgress && a.StartedAt >= task.CreatedAt && a.DeadlineAt > clock.UtcNow);
            if (!active) task.Status = "missed";
        }
        // VI: Lưu missed trước khi thêm task tiếp nối để giải phóng unique pending-node. EN: Flush missed states before inserting their successors under the pending-node unique index.
        await db.SaveChangesAsync();
    }
}
