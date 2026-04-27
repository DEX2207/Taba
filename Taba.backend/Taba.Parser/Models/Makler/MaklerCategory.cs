namespace Taba.Parser.Models.Makler;

public class MaklerCategory
{
    /// <summary>Slug категории в URL: "real-estate/real-estate-for-sale/apartments-for-sale"</summary>
    public string Slug { get; set; } = string.Empty;
 
    /// <summary>Название категории на русском</summary>
    public string Name { get; set; } = string.Empty;
 
    /// <summary>Slug родительской категории (рубрика)</summary>
    public string? ParentSlug { get; set; }
 
    /// <summary>Slug раздела верхнего уровня (Недвижимость, Транспорт...)</summary>
    public string SectionSlug { get; set; } = string.Empty;
 
    /// <summary>Название раздела верхнего уровня</summary>
    public string SectionName { get; set; } = string.Empty;
 
    /// <summary>
    /// true — категория является родителем (имеет подкатегории).
    /// Такие категории парсим после дочерних, т.к. могут содержать те же объявления.
    /// </summary>
    public bool IsParent { get; set; } = false;
}