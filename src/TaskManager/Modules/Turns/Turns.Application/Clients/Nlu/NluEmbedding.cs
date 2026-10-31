namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Вектор смысла, посчитанный по <c>description</c>. Модель и размерность приходят от
/// <c>nlu</c>; допустимая размерность зафиксирована (<c>EMBEDDING_DIM=1536</c>).
/// </summary>
public sealed record NluEmbedding(
    string Model,
    int Dim,
    IReadOnlyList<double> Values);
