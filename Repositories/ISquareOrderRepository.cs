using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface ISquareOrderRepository
    {
        Task UpsertOrderAsync(
            SquareOrderHeader order,
            long pollRunId,
            CancellationToken cancellationToken = default);

        Task DeleteOrderLinesAsync(
            string squareOrderId,
            CancellationToken cancellationToken = default);

        Task InsertOrderLineAsync(
            SquareOrderLineRecord orderLine,
            CancellationToken cancellationToken = default);

        Task CompleteOrderRefreshAsync(
            string squareOrderId,
            CancellationToken cancellationToken = default);
    }
}
