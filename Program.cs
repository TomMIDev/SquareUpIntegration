using System.Globalization;
using DataAccessUtility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Square;
using SquareUpIntegration.Repositories;
using SquareUpIntegration.Services;

var builder = Host.CreateApplicationBuilder(args);

Console.WriteLine(
    $"Host environment: {builder.Environment.EnvironmentName}");

// Register the Square API client once for the lifetime of the application.
builder.Services.AddSingleton<SquareClient>(serviceProvider =>
{
    var configuration =
        serviceProvider.GetRequiredService<IConfiguration>();

    var accessToken =
        configuration["Square:AccessToken"];

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        throw new InvalidOperationException(
            "Square access token has not been configured.");
    }

    Console.WriteLine("Square environment: Sandbox");

    return new SquareClient(
        accessToken,
        new ClientOptions
        {
            BaseUrl = SquareEnvironment.Sandbox
        });
});

// Square API services.
builder.Services.AddScoped<SquareOrderService>();
builder.Services.AddScoped<SquareProductService>();

// SQL access.
builder.Services.AddScoped<IDataAccess>(serviceProvider =>
{
    var configuration =
        serviceProvider.GetRequiredService<IConfiguration>();

    var connectionString =
        configuration.GetConnectionString("SquareUp");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "SquareUp database connection string has not been configured.");
    }

    return new SqlDataAccess(connectionString);
});

// Reference-data repositories/services.
builder.Services.AddScoped<
    ISquareReferenceDataRepository,
    SquareReferenceDataRepository>();

builder.Services.AddScoped<SquareReferenceDataService>();

// Order staging repositories/services.
builder.Services.AddScoped<
    ISquareOrderRepository,
    SquareOrderRepository>();

builder.Services.AddScoped<SquareOrderPersistenceService>();

// Poll-run repositories/services.
builder.Services.AddScoped<
    IPollRunRepository,
    PollRunRepository>();

builder.Services.AddScoped<PollRunService>();

// Pending transaction repository.
builder.Services.AddScoped<
    ICbeTransactionRepository,
    CbeTransactionRepository>();

// BO-format preview only.
// This does not invoke the CBE integration library.
builder.Services.AddScoped<CbeReceiptPreviewFormatter>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var squareClient =
    scope.ServiceProvider
        .GetRequiredService<SquareClient>();

var squareOrderService =
    scope.ServiceProvider
        .GetRequiredService<SquareOrderService>();

var squareProductService =
    scope.ServiceProvider
        .GetRequiredService<SquareProductService>();

var squareReferenceDataService =
    scope.ServiceProvider
        .GetRequiredService<SquareReferenceDataService>();

var squareReferenceDataRepository =
    scope.ServiceProvider
        .GetRequiredService<ISquareReferenceDataRepository>();

var squareOrderPersistenceService =
    scope.ServiceProvider
        .GetRequiredService<SquareOrderPersistenceService>();

var pollRunService =
    scope.ServiceProvider
        .GetRequiredService<PollRunService>();

var cbeTransactionRepository =
    scope.ServiceProvider
        .GetRequiredService<ICbeTransactionRepository>();

var cbeReceiptPreviewFormatter =
    scope.ServiceProvider
        .GetRequiredService<CbeReceiptPreviewFormatter>();

try
{
    /*
        ============================================================
        REFERENCE DATA
        ============================================================
    */

    var locationResponse =
        await squareClient.Locations.ListAsync();

    var isSandbox =
        string.Equals(
            builder.Configuration["Square:Environment"],
            "Sandbox",
            StringComparison.OrdinalIgnoreCase);

    var locations =
        locationResponse.Locations?
            .Where(location =>
                !isSandbox ||
                !string.Equals(
                    location.Name,
                    "Default Test Account",
                    StringComparison.OrdinalIgnoreCase))
            .Where(location =>
                !string.IsNullOrWhiteSpace(location.Id))
            .ToList()
        ?? [];

    if (locations.Count == 0)
    {
        Console.WriteLine(
            "Square connection succeeded, but no usable locations were returned.");

        return;
    }

    Console.WriteLine();
    Console.WriteLine(
        $"Usable Square locations: {locations.Count}");

    var products =
        await squareProductService.GetProductsAsync();

    Console.WriteLine(
        $"Catalogue variations returned: {products.Count}");

    var referenceDataSaveResult =
        await squareReferenceDataService.SaveAsync(
            locations,
            products);

    Console.WriteLine();
    Console.WriteLine("Reference data saved:");
    Console.WriteLine(
        $"  Locations: {referenceDataSaveResult.LocationsSaved}");
    Console.WriteLine(
        $"  Catalogue variations: " +
        $"{referenceDataSaveResult.CatalogVariationsSaved}");

    /*
        Keep the current development mapping rule:
        Square SKU -> BO product code.

        This can later be moved behind its own service if required.
    */
    var productMappingsSaved = 0;
    var productMappingsSkipped = 0;

    foreach (var product in products)
    {
        if (string.IsNullOrWhiteSpace(product.VariationId))
        {
            productMappingsSkipped++;
            continue;
        }

        if (string.IsNullOrWhiteSpace(product.Sku) ||
            !int.TryParse(
                product.Sku.Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var boProductCode))
        {
            productMappingsSkipped++;

            Console.WriteLine(
                $"Product mapping skipped for " +
                $"{product.ItemName ?? "<unnamed item>"} " +
                $"({product.VariationId}): " +
                $"SKU '{product.Sku ?? "<not supplied>"}' " +
                "is not a valid BO product code.");

            continue;
        }

        await squareReferenceDataRepository
            .SetProductMappingAsync(
                product.VariationId,
                boProductCode,
                product.IsActive);

        productMappingsSaved++;
    }

    Console.WriteLine();
    Console.WriteLine("Product mappings:");
    Console.WriteLine(
        $"  Saved: {productMappingsSaved}");
    Console.WriteLine(
        $"  Skipped: {productMappingsSkipped}");

    /*
        ============================================================
        COLLECTION DATE
        ============================================================

        Orders are downloaded/staged the day before collection.

        Once an order has been staged locally, injection readiness is
        controlled by InjectionStatusId rather than CollectionDate.
    */

    var ukTimeZone = GetUkTimeZone();

    var ukNow =
        TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow,
            ukTimeZone);

    var collectionDate =
        DateOnly.FromDateTime(
            ukNow.DateTime.AddDays(1));

    Console.WriteLine();
    Console.WriteLine(
        $"Polling for orders due for collection on " +
        $"{collectionDate:dd/MM/yyyy}.");

    /*
        ============================================================
        LOCATION POLLING
        ============================================================

        One PollRun is created per Square location.

        Each staged order receives the PollRunId which supplied its
        current version.
    */

    var completedPolls = 0;
    var failedPolls = 0;
    var totalOrdersFound = 0;
    var totalOrdersStaged = 0;
    var totalOrderLinesStaged = 0;

    foreach (var location in locations)
    {
        var squareLocationId = location.Id!;

        Console.WriteLine();
        Console.WriteLine(new string('-', 70));
        Console.WriteLine(
            $"Polling: {location.Name ?? "<unnamed location>"}");
        Console.WriteLine(
            $"Location ID: {squareLocationId}");

        long? pollRunId = null;

        try
        {
            /*
                Find the previous successful poll before starting the new
                PollRun.

                For subsequent polls we deliberately use StartedAtUtc
                rather than CompletedAtUtc. This creates a small overlap
                between polls and avoids missing an order amended while
                the previous poll was still running.
            */
            var lastSuccessfulPoll =
                await pollRunService.GetLastSuccessfulAsync(
                    squareLocationId,
                    collectionDate);

            pollRunId =
                await pollRunService.StartAsync(
                    squareLocationId,
                    collectionDate);

            IReadOnlyList<Order> locationOrders;

            if (lastSuccessfulPoll == null)
            {
                Console.WriteLine(
                    "No previous successful poll found. " +
                    "Running initial order search.");

                locationOrders =
                    await squareOrderService.GetOrdersAsync(
                        [squareLocationId]);
            }
            else
            {
                var updatedSinceUtc =
                    new DateTimeOffset(
                        DateTime.SpecifyKind(
                            lastSuccessfulPoll.StartedAtUtc,
                            DateTimeKind.Utc));

                Console.WriteLine(
                    $"Previous successful poll started at " +
                    $"{updatedSinceUtc:dd/MM/yyyy HH:mm:ss} UTC.");

                Console.WriteLine(
                    "Searching Square for orders updated since that time.");

                locationOrders =
                    await squareOrderService.GetOrdersAsync(
                        [squareLocationId],
                        updatedSinceUtc);
            }

            /*
                Square's UPDATED_AT filter tells us what has changed.

                The collection-date rule is then applied locally so only
                orders due for the required UK collection date are staged.
            */
            var ordersForCollection =
                squareOrderService.GetOrdersForCollectionDate(
                    locationOrders,
                    collectionDate);

            var ordersFound =
                ordersForCollection.Count;

            totalOrdersFound +=
                ordersFound;

            Console.WriteLine(
                $"Orders found for collection date: {ordersFound}");

            var ordersStaged = 0;
            var orderLinesStaged = 0;

            if (ordersFound > 0)
            {
                var persistenceResult =
                    await squareOrderPersistenceService.SaveAsync(
                        ordersForCollection,
                        pollRunId.Value);

                ordersStaged =
                    persistenceResult.OrdersSaved;

                orderLinesStaged =
                    persistenceResult.OrderLinesSaved;

                totalOrdersStaged +=
                    ordersStaged;

                totalOrderLinesStaged +=
                    orderLinesStaged;
            }

            await pollRunService.CompleteAsync(
                pollRunId.Value,
                ordersFound,
                ordersStaged);

            completedPolls++;

            Console.WriteLine(
                $"Orders staged: {ordersStaged}");

            Console.WriteLine(
                $"Order lines staged: {orderLinesStaged}");

            Console.WriteLine(
                $"PollRun {pollRunId.Value} completed.");
        }
        catch (Exception ex)
        {
            failedPolls++;

            if (pollRunId.HasValue)
            {
                try
                {
                    await pollRunService.FailAsync(
                        pollRunId.Value,
                        ex);
                }
                catch (Exception failPollException)
                {
                    Console.WriteLine(
                        $"Unable to mark PollRun {pollRunId.Value} " +
                        $"as failed: {failPollException.Message}");
                }
            }

            Console.WriteLine(
                $"Polling failed for location " +
                $"{location.Name ?? squareLocationId}: {ex.Message}");
        }
    }

    /*
        ============================================================
        RUN SUMMARY
        ============================================================
    */

    Console.WriteLine();
    Console.WriteLine(new string('=', 70));
    Console.WriteLine("Polling complete.");
    Console.WriteLine(
        $"  Successful location polls: {completedPolls}");
    Console.WriteLine(
        $"  Failed location polls: {failedPolls}");
    Console.WriteLine(
        $"  Orders found: {totalOrdersFound}");
    Console.WriteLine(
        $"  Orders staged: {totalOrdersStaged}");
    Console.WriteLine(
        $"  Order lines staged: {totalOrderLinesStaged}");

    /*
        ============================================================
        PREVIEW PENDING BO RECEIPT DATA
        ============================================================

        This stage does not invoke CBETransactionIntegration.

        It reads locally staged Pending orders and formats them into
        the same master/line shape expected by the store BO process.

        No Receipts.Push call is made and InjectionStatusId is not
        changed here.
    */

    var pendingTransactions =
        await cbeTransactionRepository
            .GetPendingTransactionsAsync();

    Console.WriteLine();
    Console.WriteLine(new string('=', 70));
    Console.WriteLine(
        $"Pending CBE transactions: {pendingTransactions.Count}");

    foreach (var transaction in pendingTransactions)
    {
        var preview =
            cbeReceiptPreviewFormatter.Build(
                transaction);

        cbeReceiptPreviewFormatter.WriteToConsole(
            preview);
    }
}
catch (SquareApiException ex)
{
    Console.WriteLine("Square API request failed.");
    Console.WriteLine(
        $"Status code: {ex.StatusCode}");
    Console.WriteLine(
        $"Message: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine("Application error.");
    Console.WriteLine(ex.Message);
}

static TimeZoneInfo GetUkTimeZone()
{
    try
    {
        return TimeZoneInfo.FindSystemTimeZoneById(
            "GMT Standard Time");
    }
    catch (TimeZoneNotFoundException)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/London");
    }
}
