using TodayChecklist.Models;

namespace TodayChecklist.ViewModels;

public sealed class DayDividerViewModel(DayPeriod? period) : ScheduleRowViewModel
{
    public DayPeriod? Period { get; } = period;

    public override string RowKey => "divider:" + Period;

    public override bool CanDrag => Period.HasValue;

    public string Title => Period switch
    {
        DayPeriod.Morning => "上午",
        DayPeriod.Afternoon => "下午",
        DayPeriod.Evening => "晚上",
        _ => "已完成",
    };
}
