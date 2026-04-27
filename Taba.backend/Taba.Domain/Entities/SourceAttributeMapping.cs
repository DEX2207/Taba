namespace Taba.Domain.Entities;

public class SourceAttributeMapping
{
    public int Id { get; set; }

    /// <summary>
    /// Источник (999.md, makler.md, ...)
    /// </summary>
    public int SourceId { get; set; }

    /// <summary>
    /// Сырой ключ атрибута как он приходит с источника.
    /// Для makler.md — румынское название: "Numărul de camere", "Marca"
    /// Для 999.md — строковый ключ feature: "brand", "year"
    /// </summary>
    public string RawKey { get; set; } = string.Empty;

    /// <summary>
    /// Нормализованный ключ — единый для всех источников.
    /// Совпадает с CategoryFilter.Key, по нему работают фильтры.
    /// null = маппинг ещё не определён (новый атрибут, требует обработки)
    /// </summary>
    public string? NormalizedKey { get; set; }

    /// <summary>
    /// Привязка к категории. null = маппинг глобальный для источника.
    /// Используется когда один RawKey в разных категориях означает разное.
    /// Например: "Tip" в недвижимости = "property_type", в авто = "body_type"
    /// </summary>
    public int? CategoryId { get; set; }

    /// <summary>
    /// Уверенность автоматического маппинга от 0.0 до 1.0.
    /// 1.0 = проставлен вручную или точное совпадение.
    /// null = ещё не обработан.
    /// </summary>
    public float? Confidence { get; set; }

    /// <summary>
    /// Атрибут был смаплен вручную через UI — не перезаписывать автоматикой.
    /// </summary>
    public bool IsManual { get; set; } = false;

    // Navigation properties
    public Source Source { get; set; } = null!;
    public Category? Category { get; set; }
}