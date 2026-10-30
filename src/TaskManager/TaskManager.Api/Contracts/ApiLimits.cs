namespace TaskManager.Api.Contracts;

/// <summary>
/// Границы запросов из <c>docs/contracts/http-api.md</c> §6: текст до 1000 символов,
/// файл до 10 МБ. Это часть контракта, а не настройка развёртывания, поэтому значения
/// не читаются из конфигурации, а сообщения об ошибке называют их же.
/// </summary>
public static class ApiLimits
{
    public const int MaxTextLength = 1000;

    public const int MaxUploadBytes = 10 * 1024 * 1024;

    public static string MaxUploadMegabytes => $"{MaxUploadBytes / (1024 * 1024)} МБ";
}
