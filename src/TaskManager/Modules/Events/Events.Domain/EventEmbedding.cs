namespace TaskManager.Events.Domain;

/// <summary>
/// Зафиксированный контракт эмбеддинга события. Модель и размерность совпадают с
/// <c>EMBEDDING_MODEL</c>/<c>EMBEDDING_DIM</c>, колонками <c>vector(1536)</c> и
/// HNSW-индексом.
/// </summary>
public static class EventEmbedding
{
    public const string Model = "text-embedding-3-small";

    public const int Dimensions = 1536;
}
