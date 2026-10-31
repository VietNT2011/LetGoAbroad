using System.Text.Json;
using EnglishHub.Application;
using EnglishHub.Domain;
using EnglishHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnglishHub.Infrastructure.Learning;

public sealed record StartListening(Guid ClientRequestId, string? ExpectedResumeEtag = null);
public sealed record CheckDictation(Guid ClientRequestId, string AnswerText);
public sealed record RevealTranscript(Guid ClientRequestId);
public sealed record SaveListeningPosition(Guid ClientRequestId, string ExpectedEtag, long PositionMs);
public sealed record ListeningPositionAck(Guid SessionId, long PositionMs, string Etag);
public sealed record TranscriptRevealView(Guid Id, string TextEn, string TextVi, bool IsDisclosed);
public sealed record DictationResult(Guid Id, Guid SegmentId, int Number, string AnswerText, bool IsCorrect, bool IsAssisted, string ExpectedText, DictationToken[] Tokens);
public sealed record ListeningSegmentView(Guid Id, int Order, long StartMs, long EndMs, string? TextEn, string? TextVi, bool IsDisclosed, DictationResult? Latest);
public sealed record ListeningView(Guid Id, Guid ContentVersionId, Guid MediaAssetId, long DurationMs, string Title, DateTimeOffset ExpiresAt, DateTimeOffset ServerNow, bool IsCurrent, ListeningPositionAck Resume, int AnsweredSegments, int FirstUnaidedCount, int FirstAssistedCount, int FirstCorrectUnaided, int FirstCorrectAssisted, ListeningSegmentView[] Segments);

/// <summary>VI: Session/đáp án là nguồn luyện nghe riêng, không tự hoàn thành lesson hoặc tạo điểm thi. EN: Listening practice owns its evidence without creating lesson or assessment completion.</summary>
public sealed class ListeningService(HubDbContext db, Transactions tx, Receipts receipts, IClock clock)
{
    public async Task<object> Info(Guid user, Guid version)
    {
        var lesson = await db.ContentVersions.AsNoTracking().Where(x => x.Id == version && x.Root.Kind == ContentKind.Lesson && (x.Status == PublicationStatus.Published || x.Status == PublicationStatus.Archived && db.ListeningSessions.Any(s => s.UserId == user && s.ContentVersionId == version)))
            .Select(x => new { x.Id, x.Title, x.Status, x.IsFixture, x.MediaAssetId, segmentCount = x.Segments.Count, durationMs = db.MediaAssets.Where(m => m.Id == x.MediaAssetId && m.Purpose == "lesson_audio" && m.Status == MediaStatus.Ready).Select(m => m.DurationMs).SingleOrDefault() }).SingleOrDefaultAsync() ?? throw new DomainError(404, "lesson_not_found", "Không tìm thấy bài nghe.");
        var head = await db.ListeningResumes.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user && x.ContentVersionId == version);
        return new { lesson.Id, lesson.Title, lesson.MediaAssetId, lesson.segmentCount, lesson.durationMs, canStart = lesson.Status == PublicationStatus.Published && !lesson.IsFixture && lesson.segmentCount > 0 && lesson.durationMs > 0,
            sessionId = head?.SessionId, positionMs = head?.PositionMs ?? 0, resumeEtag = head is null ? null : Etags.Of(head) };
    }

    public Task<Guid> Start(Guid user, Guid version, StartListening input) => tx.Run($"listening:{user}:{version}", async () =>
    {
        const string scope = "listening-start"; var hash = Receipts.Hash(new { version, input });
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash); if(replay is not null) return replay.ResourceId;
        var lesson = await db.ContentVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == version && x.Root.Kind == ContentKind.Lesson && x.Status == PublicationStatus.Published && !x.IsFixture && x.Segments.Any()) ?? throw new DomainError(404, "lesson_not_found", "Bài nghe chưa có transcript hợp lệ đã publish.");
        var media = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == lesson.MediaAssetId && x.Purpose == "lesson_audio" && x.Status == MediaStatus.Ready && x.DurationMs > 0) ?? throw new DomainError(409, "media_not_ready", "Audio bài nghe hiện chưa khả dụng.");
        var head = await db.ListeningResumes.SingleOrDefaultAsync(x => x.UserId == user && x.ContentVersionId == version);
        if(head is null) DomainError.Require(input.ExpectedResumeEtag is null, 412, "stale_version", "Vị trí nghe đã thay đổi. Hãy tải lại."); else Etags.Check(head, input.ExpectedResumeEtag);
        var session = new ListeningSession { UserId = user, ContentVersionId = version, MediaAssetId = media.Id, CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddHours(24) };
        db.ListeningSessions.Add(session); await db.SaveChangesAsync();
        if(head is null) {head = new ListeningResume { UserId = user, ContentVersionId = version }; db.ListeningResumes.Add(head);}
        head.SessionId = session.Id; head.PositionMs = Math.Min(head.PositionMs, media.DurationMs!.Value);
        receipts.Save(user, scope, input.ClientRequestId, hash, session.Id, new { session.Id }); return session.Id;
    });

    public Task<ListeningView> Read(Guid user, Guid id) => WithSession(user,id,session => ReadSnapshot(user,id,session));

    private async Task<ListeningView> ReadSnapshot(Guid user, Guid id, ListeningSession session)
    {
        var segments = await db.TranscriptSegments.AsNoTracking().Where(x => x.ContentVersionId == session.ContentVersionId).OrderBy(x => x.Order).ToArrayAsync();
        var segmentIds = segments.Select(x => x.Id).ToArray();
        var disclosed = (await db.ListeningDisclosures.AsNoTracking().Where(x => x.UserId == user && segmentIds.Contains(x.SegmentId)).Select(x => x.SegmentId).ToArrayAsync()).ToHashSet();
        // VI: Chỉ lấy câu trả lời mới nhất mỗi đoạn, không tải mọi bản nháp cũ. EN: Fetch at most one latest response per segment instead of every historical answer body.
        var latest = await db.SegmentAnswers.AsNoTracking().Where(x => x.SessionId == id && !db.SegmentAnswers.Any(newer => newer.SessionId == id && newer.SegmentId == x.SegmentId && newer.Number > x.Number)).ToDictionaryAsync(x => x.SegmentId);
        var first = await db.SegmentAnswers.AsNoTracking().Where(x => x.SessionId == id && x.Number == 1).Select(x => new { x.IsCorrect, x.IsAssisted }).ToArrayAsync();
        var head = await db.ListeningResumes.AsNoTracking().SingleAsync(x => x.UserId == user && x.ContentVersionId == session.ContentVersionId);
        return new(id, session.ContentVersionId, session.MediaAssetId, session.Media.DurationMs ?? 0, session.ContentVersion.Title, session.ExpiresAt, clock.UtcNow, head.SessionId == id, new(head.SessionId, head.PositionMs, Etags.Of(head)), first.Length, first.Count(x => !x.IsAssisted), first.Count(x => x.IsAssisted), first.Count(x => x.IsCorrect && !x.IsAssisted), first.Count(x => x.IsCorrect && x.IsAssisted),
            segments.Select(segment => new ListeningSegmentView(segment.Id, segment.Order, segment.StartMs, segment.EndMs, disclosed.Contains(segment.Id) ? segment.TextEn : null, disclosed.Contains(segment.Id) ? segment.TextVi : null, disclosed.Contains(segment.Id), latest.TryGetValue(segment.Id, out var answer) ? Result(segment, answer) : null)).ToArray());
    }

    public Task<TranscriptRevealView> Reveal(Guid user, Guid id, Guid segmentId, RevealTranscript input) => WithSession(user, id, async session =>
    {
        const string scope = "listening-reveal"; var hash = Receipts.Hash(new { id, segmentId, input });
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash); if(replay is not null) return JsonSerializer.Deserialize<TranscriptRevealView>(replay.ResponseJson)!;
        RequireOpen(session); var segment = await Segment(session, segmentId); await Disclose(user, segmentId);
        var response = new TranscriptRevealView(segment.Id, segment.TextEn, segment.TextVi, true);
        receipts.Save(user, scope, input.ClientRequestId, hash, id, response); return response;
    });

    public Task<DictationResult> Answer(Guid user, Guid id, Guid segmentId, CheckDictation input) => WithSession(user, id, async session =>
    {
        const string scope = "dictation-answer"; var hash = Receipts.Hash(new { id, segmentId, input });
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash); if(replay is not null) return JsonSerializer.Deserialize<DictationResult>(replay.ResponseJson)!;
        RequireOpen(session); var segment = await Segment(session, segmentId);
        DomainError.Require(input.AnswerText.Length <= 2000, 422, "invalid_dictation_answer", "Đáp án tối đa2000 ký tự.");
        var number = await db.SegmentAnswers.CountAsync(x => x.SessionId == id && x.SegmentId == segmentId) + 1;
        DomainError.Require(number <= 20, 422, "dictation_attempt_limit", "Mỗi đoạn tối đa20 lần kiểm tra trong một session.");
        var variants = JsonSerializer.Deserialize<string[]>(segment.AcceptedAnswersJson)!;
        var answer = new SegmentAnswer { UserId = user, SessionId = id, ContentVersionId = session.ContentVersionId, SegmentId = segmentId, Number = number, AnswerText = input.AnswerText,
            IsCorrect = AnswerNormalization.Matches(input.AnswerText, variants, segment.NormalizationPolicyVersion, segment.IgnoreTerminalPunctuation), IsAssisted = await db.ListeningDisclosures.AnyAsync(x => x.UserId == user && x.SegmentId == segmentId), AnsweredAt = clock.UtcNow };
        var result = Result(segment, answer); // VI: So sánh token được kiểm giới hạn trước mọi ghi. EN: Validate bounded token comparison before writing any state.
        db.SegmentAnswers.Add(answer); await Disclose(user, segmentId);
        receipts.Save(user, scope, input.ClientRequestId, hash, id, result); return result;
    });

    public Task<ListeningPositionAck> SavePosition(Guid user, Guid id, SaveListeningPosition input) => WithSession(user, id, async session =>
    {
        const string scope = "listening-position"; var hash = Receipts.Hash(new { id, input });
        var replay = await receipts.Find(user, scope, input.ClientRequestId, hash); if(replay is not null) return JsonSerializer.Deserialize<ListeningPositionAck>(replay.ResponseJson)!;
        RequireOpen(session);
        var head = await db.ListeningResumes.SingleAsync(x => x.UserId == user && x.ContentVersionId == session.ContentVersionId);
        DomainError.Require(head.SessionId == id, 409, "listening_session_changed", "Tab khác đã chọn phiên nghe mới. Hãy tải phiên hiện hành."); Etags.Check(head, input.ExpectedEtag);
        DomainError.Require(input.PositionMs >= 0 && input.PositionMs <= session.Media.DurationMs, 422, "invalid_playback_position", "Vị trí nằm ngoài audio.");
        head.PositionMs = input.PositionMs; await db.SaveChangesAsync(); var ack = new ListeningPositionAck(id, head.PositionMs, Etags.Of(head));
        receipts.Save(user, scope, input.ClientRequestId, hash, id, ack); return ack;
    });

    private async Task<T> WithSession<T>(Guid user, Guid id, Func<ListeningSession,Task<T>> action)
    {
        var version = await db.ListeningSessions.AsNoTracking().Where(x => x.Id == id && x.UserId == user).Select(x => (Guid?)x.ContentVersionId).SingleOrDefaultAsync() ?? throw new DomainError(404, "listening_not_found", "Không tìm thấy phiên nghe của bạn.");
        return await tx.Run($"listening:{user}:{version}", async () => await action(await Owned(user,id)));
    }
    private async Task<ListeningSession> Owned(Guid user, Guid id) => await db.ListeningSessions.AsNoTracking().Include(x => x.ContentVersion).Include(x => x.Media).SingleOrDefaultAsync(x => x.Id == id && x.UserId == user) ?? throw new DomainError(404,"listening_not_found","Không tìm thấy phiên nghe của bạn.");
    private async Task<TranscriptSegment> Segment(ListeningSession session, Guid id) => await db.TranscriptSegments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.ContentVersionId == session.ContentVersionId) ?? throw new DomainError(404,"segment_not_found","Đoạn không thuộc phiên nghe.");
    private void RequireOpen(ListeningSession session) => DomainError.Require(clock.UtcNow < session.ExpiresAt,409,"listening_expired","Phiên nghe đã hết hạn. Bản cũ vẫn được giữ; hãy tạo phiên mới để luyện tiếp.");
    private async Task Disclose(Guid user,Guid id) {if(!await db.ListeningDisclosures.AnyAsync(x => x.UserId == user && x.SegmentId == id)) db.ListeningDisclosures.Add(new ListeningDisclosure { UserId = user, SegmentId = id, FirstDisclosedAt = clock.UtcNow });}
    private static DictationResult Result(TranscriptSegment segment,SegmentAnswer answer)
    {
        var expected = answer.IsCorrect ? JsonSerializer.Deserialize<string[]>(segment.AcceptedAnswersJson)!.First(x => AnswerNormalization.Matches(answer.AnswerText,[x],segment.NormalizationPolicyVersion,segment.IgnoreTerminalPunctuation)) : segment.TextEn;
        return new(answer.Id,answer.SegmentId,answer.Number,answer.AnswerText,answer.IsCorrect,answer.IsAssisted,expected,DictationDiff.Compare(expected,answer.AnswerText,segment.NormalizationPolicyVersion,segment.IgnoreTerminalPunctuation));
    }
}
