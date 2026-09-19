using TodayChecklist.Models;

namespace TodayChecklist.Services;

public static class DocumentValidator
{
    public static void Validate(TaskDataDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != TaskDataDocument.CurrentSchemaVersion)
        {
            throw new DocumentValidationException($"不支持的数据版本：{document.SchemaVersion}。");
        }

        if (document.Tasks is null || document.Recurrences is null)
        {
            throw new DocumentValidationException("任务或重复规则集合缺失。");
        }

        var recurrenceIds = new HashSet<Guid>();
        foreach (var recurrence in document.Recurrences)
        {
            if (recurrence.Id == Guid.Empty || !recurrenceIds.Add(recurrence.Id))
            {
                throw new DocumentValidationException("重复规则 ID 无效或重复。");
            }

            ValidateText(recurrence.Title, recurrence.Notes);
            if (recurrence.Kind == RepeatKind.None)
            {
                throw new DocumentValidationException("重复规则不能使用 None 类型。");
            }

            if (recurrence.Kind == RepeatKind.Weekly && recurrence.Weekday is null)
            {
                throw new DocumentValidationException("每周规则缺少星期信息。");
            }

            if (recurrence.Kind == RepeatKind.Monthly && recurrence.DayOfMonth is not (>= 1 and <= 31))
            {
                throw new DocumentValidationException("每月规则的日期必须在 1 到 31 之间。");
            }
        }

        var taskIds = new HashSet<Guid>();
        foreach (var task in document.Tasks)
        {
            if (task.Id == Guid.Empty || !taskIds.Add(task.Id))
            {
                throw new DocumentValidationException("任务 ID 无效或重复。");
            }

            ValidateText(task.Title, task.Notes);
            if (!Enum.IsDefined(task.Period))
            {
                throw new DocumentValidationException("任务时段无效。");
            }
            if (task.OriginalDate == default || task.ScheduledDate == default)
            {
                throw new DocumentValidationException("任务日期缺失。");
            }

            if (task.RolloverDays < 0 || task.SortOrder < 0)
            {
                throw new DocumentValidationException("任务排序或跨日数据无效。");
            }

            if (task.IsCompleted != task.CompletedAtUtc.HasValue)
            {
                throw new DocumentValidationException("任务完成状态与完成时间不一致。");
            }

            if (task.RecurrenceId is Guid recurrenceId && !recurrenceIds.Contains(recurrenceId))
            {
                throw new DocumentValidationException("任务引用了不存在的重复规则。");
            }
        }
    }

    public static void Validate(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new DocumentValidationException($"不支持的设置版本：{settings.SchemaVersion}。");
        }

        if (settings.Opacity is < 0.75 or > 1.0)
        {
            throw new DocumentValidationException("透明度必须在 0.75 到 1.0 之间。");
        }

        if (!double.IsFinite(settings.WindowWidth) || !double.IsFinite(settings.WindowHeight)
            || settings.WindowWidth < 280 || settings.WindowHeight < 360)
        {
            throw new DocumentValidationException("窗口尺寸无效。");
        }
    }

    private static void ValidateText(string? title, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw new DocumentValidationException("任务标题必须为 1 到 200 个字符。");
        }

        if (notes is null || notes.Length > 2_000)
        {
            throw new DocumentValidationException("任务备注不能超过 2,000 个字符。");
        }
    }
}
