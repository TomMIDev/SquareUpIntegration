using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface ICbeTransactionRepository
    {
        Task<IReadOnlyList<CbeTransactionRequest>>
            GetPendingTransactionsAsync(
                CancellationToken cancellationToken = default);
    }
}
