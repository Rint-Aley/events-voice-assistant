namespace TaskManager.Turns.Application.Clients.Nlu;

/// <summary>
/// Вектор смысла, посчитанный по <c>description</c>. Модель и размерность приходят от
/// <c>nlu</c>; до фиксации <c>EMBEDDING_MODEL</c>/<c>EMBEDDING_DIM</c> домен их не хранит.
/// </summary>
public sealed record NluEmbedding(
    string Model,
    int Dim,
    IReadOnlyList<double> Values);
