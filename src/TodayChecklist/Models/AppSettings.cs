namespace TodayChecklist.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ThemeMode Theme { get; set; } = ThemeMode.System;

    public double Opacity { get; set; } = 0.92;

    public DisplayMode DisplayMode { get; set; } = DisplayMode.Standard;

    public bool StartupEnabled { get; set; }

    public bool CloseToTray { get; set; } = true;

    public bool ShowRolloverDays { get; set; } = true;

    public bool ShowDayDividers { get; set; }

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double WindowWidth { get; set; } = 360;

    public double WindowHeight { get; set; } = 560;

    public string? MonitorDeviceName { get; set; }
}
