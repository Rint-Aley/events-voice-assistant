namespace TaskManager.SharedKernel;

/// <summary>
/// Источник времени для домена и сценариев. Зона задаётся конфигурацией <c>TIMEZONE</c>.
/// </summary>
public interface IClock
{
    /// <summary>Текущий момент в UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Зона, в которой интерпретируются локальные дата и время (<c>TIMEZONE</c>).</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Текущий момент в <see cref="TimeZone"/>.</summary>
    DateTimeOffset LocalNow { get; }
}
