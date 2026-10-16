namespace Workspace1C.Models;

/// <summary>
/// Сведения о конфигурации информационной базы 1С: наименование и версия.
/// </summary>
public readonly record struct OneCConfigInfo(string Name, string Version);