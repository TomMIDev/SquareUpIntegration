using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface ICbeTransactionRepository
    {
        Task<IReadOnlyList<CbeTransactionRequest>> GetReadyTransactionsAsync(
            DateOnly asOfDate,
            CancellationToken cancellationToken = default);
    }
}
