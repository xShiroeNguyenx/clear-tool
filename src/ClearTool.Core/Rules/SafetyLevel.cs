namespace ClearTool.Core.Rules;

public enum SafetyLevel
{
    /// <summary>Xóa an toàn — cache/temp tái tạo được.</summary>
    Safe,

    /// <summary>Xóa được nhưng cần cân nhắc (tải/cài lại được, mất config...).</summary>
    Caution,

    /// <summary>Hệ thống — không bao giờ gợi ý xóa.</summary>
    Keep,

    /// <summary>Không khớp rule nào.</summary>
    Unknown,
}
