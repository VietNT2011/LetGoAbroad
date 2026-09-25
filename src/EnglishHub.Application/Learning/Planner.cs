namespace EnglishHub.Application.Learning;
public sealed record PlanCandidate(Guid ContentVersionId, int Minutes, string Kind, int Order);

/// <summary>VI: Planner v1 xác định sẵn, không gọi AI. EN: A deterministic, budget-constrained planner with explainable rules.</summary>
public static class Planner
{
    public static IReadOnlyList<PlanCandidate> Select(IEnumerable<PlanCandidate> lessons, IEnumerable<PlanCandidate> practices, int budget)
    {
        var selected = new List<PlanCandidate>();
        var lessonQueue = new Queue<PlanCandidate>(lessons.OrderBy(x => x.Order));
        if (lessonQueue.TryPeek(out var next) && next.Minutes <= budget) { selected.Add(lessonQueue.Dequeue()); budget -= next.Minutes; }
        var practice = practices.OrderBy(x => x.Order).FirstOrDefault(x => x.Minutes <= budget);
        if (practice is not null) { selected.Add(practice); budget -= practice.Minutes; }
        while (selected.Count < 3 && lessonQueue.TryDequeue(out var lesson))
            if (lesson.Minutes <= budget) { selected.Add(lesson); budget -= lesson.Minutes; }
        return selected;
    }
    public static string PlacementLevel(int correct, int total) => total <= 0 || correct * 100 / total < 40 ? "foundation" : correct * 100 / total < 75 ? "core" : "exam_practice";
}
