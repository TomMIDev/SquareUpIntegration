using SquareUpIntegration.Models;

namespace SquareUpIntegration.Repositories
{
    public interface ISquareOrderRepository
    {
        Task BeginOrderRefreshAsync(
            SquareOrderHeader order,
            Guid refreshToken,
            long? pollRunId = null,
            CancellationToken cancellationToken = default);

        Task UpsertOrderFulfillmentAsync(
            SquareOrderFulfillmentRecord fulfillment,
            Guid refreshToken,
            CancellationToken cancellationToken = default);

        Task UpsertOrderLineAsync(
            SquareOrderLineRecord orderLine,
            Guid refreshToken,
            CancellationToken cancellationToken = default);

        Task CompleteOrderRefreshAsync(
            string squareOrderId,
            Guid refreshToken,
            CancellationToken cancellationToken = default);
    }
}
