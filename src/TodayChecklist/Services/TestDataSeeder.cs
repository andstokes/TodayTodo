using TodayChecklist.Models;

namespace TodayChecklist.Services;

public static class TestDataSeeder
{
    public static bool SeedIfEmpty(TaskDataDocument document, TaskService taskService, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(taskService);
        if (document.Tasks.Count != 0 || document.Recurrences.Count != 0)
        {
            return false;
        }

        _ = taskService.AddTask(document, "拖动我测试排序", "这是隔离测试数据，不会进入正式清单。", today, RepeatKind.None);
        _ = taskService.AddTask(document, "双击我测试编辑", string.Empty, today, RepeatKind.None);
        var completedToday = taskService.AddTask(document, "今天已完成的示例", string.Empty, today, RepeatKind.None);
        taskService.SetCompleted(completedToday, completed: true);

        var yesterday = taskService.AddTask(
            document,
            "昨天完成的历史示例",
            "用于检查历史记录中的正常文字和备注。",
            today.AddDays(-1),
            RepeatKind.None);
        taskService.SetCompleted(yesterday, completed: true);

        var earlier = taskService.AddTask(
            document,
            "更早完成的历史示例",
            string.Empty,
            today.AddDays(-3),
            RepeatKind.None);
        taskService.SetCompleted(earlier, completed: true);
        return true;
    }
}
