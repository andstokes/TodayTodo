using TodayChecklist.Models;

namespace TodayChecklist.Services;

public static class StorageValidation
{
    public static void Validate(StorageLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (locator.SchemaVersion != StorageLocator.CurrentSchemaVersion)
        {
            throw new DocumentValidationException($"不支持的数据位置版本：{locator.SchemaVersion}。");
        }

        if (locator.StoreId == Guid.Empty || string.IsNullOrWhiteSpace(locator.DataRoot) || !Path.IsPathFullyQualified(locator.DataRoot))
        {
            throw new DocumentValidationException("数据位置配置无效。");
        }

        if (new Uri(Path.GetFullPath(locator.DataRoot)).IsUnc)
        {
            throw new DocumentValidationException("首版不支持网络数据路径。");
        }
    }

    public static void Validate(StorageMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        if (marker.SchemaVersion != StorageMarker.CurrentSchemaVersion || marker.StoreId == Guid.Empty)
        {
            throw new DocumentValidationException("数据目录所有权标记无效。");
        }
    }
}

