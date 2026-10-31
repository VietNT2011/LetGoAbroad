using EnglishHub.Domain;

namespace EnglishHub.Application.Learning;

public sealed record GraphNode(string Key, int StudyDay, string[] Prerequisites);
public sealed record PathCandidate(Guid NodeId, Guid ContentVersionId, string Kind, int Minutes, int StudyDay, int Order, Guid? SourceTaskId = null);

/// <summary>VI: Kahn kiểm tra DAG, không đệ quy theo độ sâu dữ liệu CMS. EN: Kahn's algorithm validates a bounded DAG without recursion over authored data.</summary>
public static class PathGraph
{
    public static string[] Validate(GraphNode[] nodes)
    {
        DomainError.Require(nodes.Length is >= 1 and <= 200 && nodes.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == nodes.Length, 422, "invalid_path_nodes", "Lộ trình cần 1–200 node có key duy nhất.");
        var byKey = nodes.ToDictionary(x => x.Key, StringComparer.Ordinal);
        DomainError.Require(nodes.All(x => x.StudyDay is >= 1 and <= 365 && x.Prerequisites.Length <= 20 && x.Prerequisites.Distinct(StringComparer.Ordinal).Count() == x.Prerequisites.Length && x.Prerequisites.All(byKey.ContainsKey)), 422, "invalid_prerequisite", "Ngày học, prerequisite bị lặp hoặc tham chiếu không hợp lệ.");
        var incoming = nodes.ToDictionary(x => x.Key, x => x.Prerequisites.Length, StringComparer.Ordinal);
        var followers = nodes.ToDictionary(x => x.Key, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var node in nodes) foreach (var parent in node.Prerequisites) followers[parent].Add(node.Key);
        var ready = new SortedSet<string>(incoming.Where(x => x.Value == 0).Select(x => x.Key), StringComparer.Ordinal);
        var order = new List<string>();
        while (ready.Count > 0)
        {
            var key = ready.Min!; ready.Remove(key); order.Add(key);
            foreach (var follower in followers[key]) if (--incoming[follower] == 0) ready.Add(follower);
        }
        DomainError.Require(order.Count == nodes.Length, 422, "cyclic_path", "Prerequisite chứa chu trình; không thể publish.");
        DomainError.Require(nodes.All(x => x.Prerequisites.All(parent => byKey[parent].StudyDay <= x.StudyDay)), 422, "invalid_schedule", "Node không thể được xếp trước ngày học của prerequisite.");
        return order.ToArray();
    }
}

/// <summary>VI: Tiếp tục task bỏ lỡ trước, rồi bài nền tảng và practice vừa ngân sách. EN: Prefer eligible carryover, then a foundation lesson and a practice within the remaining budget.</summary>
public static class PathScheduler
{
    public static IReadOnlyList<PathCandidate> Select(IEnumerable<PathCandidate> candidates, int budget)
    {
        var ordered = candidates.OrderBy(x => x.StudyDay).ThenBy(x => x.Order).ThenBy(x => x.NodeId).ToArray();
        var result = new List<PathCandidate>();
        bool Add(PathCandidate candidate)
        {
            if (result.Count >= 3 || candidate.Minutes > budget || result.Any(x => x.NodeId == candidate.NodeId || x.ContentVersionId == candidate.ContentVersionId)) return false;
            result.Add(candidate); budget -= candidate.Minutes; return true;
        }
        foreach (var item in ordered.Where(x => x.SourceTaskId is not null)) Add(item);
        foreach (var kind in new[] { "lesson", "assessment" })
            foreach (var item in ordered.Where(x => x.Kind == kind)) if (Add(item)) break;
        foreach (var item in ordered) Add(item);
        return result;
    }

    public static int StudyDay(DateOnly start, DateOnly today, string studyDays)
    {
        if (today < start) return 0;
        var weekdays = studyDays.Split(',').Select(int.Parse).ToHashSet();
        var elapsed = today.DayNumber - start.DayNumber + 1;
        var count = elapsed / 7 * weekdays.Count;
        for (var day = 0; day < elapsed % 7; day++) if (weekdays.Contains((int)start.AddDays(day).DayOfWeek)) count++;
        return count;
    }
}
