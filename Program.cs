using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Square;
using SquareUpIntegration.Services;
using DataAccessUtility;
using SquareUpIntegration.Repositories;

var builder = Host.CreateApplicationBuilder(args);

Console.WriteLine($"Host environment: {builder.Environment.EnvironmentName}");

// Register the Square API client once for the lifetime of the application.
builder.Services.AddSingleton<SquareClient>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();

    var accessToken = configuration["Square:AccessToken"];

    if (string.IsNullOrWhiteSpace(accessToken))
    {
        throw new InvalidOperationException(
            "Square access token has not been configured.");
    }

    Console.WriteLine("Square environment: Sandbox");

    // Force the Square client to use the Sandbox API.
    return new SquareClient(
        accessToken,
        new ClientOptions
        {
            BaseUrl = SquareEnvironment.Sandbox
        });
});

// Register services used by the Square integration.
builder.Services.AddScoped<SquareOrderService>();
builder.Services.AddScoped<SquareProductService>();
builder.Services.AddScoped<CBETransactionService>();

// Register SQL access.
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

// Register repositories used to persist Square reference data.
builder.Services.AddScoped<
    ISquareReferenceDataRepository,
    SquareReferenceDataRepository>();

// Register the repository used to persist Square orders,
// fulfilments and order line items.
builder.Services.AddScoped<
    ISquareOrderRepository,
    SquareOrderRepository>();

// Register the service that coordinates saving Square locations
// and catalogue variations to the database.
builder.Services.AddScoped<SquareReferenceDataService>();

// Register the service that stages Square orders to the database.
builder.Services.AddScoped<SquareOrderPersistenceService>();

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var squareClient =
    scope.ServiceProvider.GetRequiredService<SquareClient>();

var squareOrderService =
    scope.ServiceProvider.GetRequiredService<SquareOrderService>();
var squareProductService =
    scope.ServiceProvider.GetRequiredService<SquareProductService>();
var squareReferenceDataService =
    scope.ServiceProvider
        .GetRequiredService<SquareReferenceDataService>();

var squareReferenceDataRepository =
    scope.ServiceProvider
        .GetRequiredService<ISquareReferenceDataRepository>();

var squareOrderPersistenceService =
    scope.ServiceProvider
        .GetRequiredService<SquareOrderPersistenceService>();

try
{
    // Retrieve the Square locations available to this Sandbox account.
    var response = await squareClient.Locations.ListAsync();

    var isSandbox = string.Equals(
        builder.Configuration["Square:Environment"],
        "Sandbox",
        StringComparison.OrdinalIgnoreCase);

    var locations = response.Locations?
        .Where(location =>
            !isSandbox ||
            !string.Equals(
                location.Name,
                "Default Test Account",
                StringComparison.OrdinalIgnoreCase))
        .ToList()
        ?? [];

    Console.WriteLine("Square connection succeeded.");
    Console.WriteLine();
    Console.WriteLine("Locations:");

    if (locations.Count == 0)
    {
        Console.WriteLine(
            "Square connection succeeded, but no usable locations were returned.");

        return;
    }

    foreach (var location in locations)
    {
        Console.WriteLine(
            $"{location.Name} - Location ID: {location.Id}");
    }


    // Retrieve products from the Square catalogue.
    // This runs before any order-related return statements.
    var products =
        await squareProductService.GetProductsAsync();

    Console.WriteLine();
    Console.WriteLine($"Products returned by Square: {products.Count}");

    foreach (var product in products)
    {
        Console.WriteLine();
        Console.WriteLine($"Item: {product.ItemName}");
        Console.WriteLine($"Item ID: {product.ItemId}");
        Console.WriteLine($"Variation: {product.VariationName}");
        Console.WriteLine($"Variation ID: {product.VariationId}");
        Console.WriteLine($"SKU: {product.Sku ?? "<not supplied>"}");

        if (product.PriceAmount is long amount)
        {
            Console.WriteLine(
                $"Price: {FormatMoney(
                    amount,
                    product.Currency)}");
        }
        else
        {
            Console.WriteLine("Price: <not supplied>");
        }
    }

    // Save the Square locations and catalogue variations already retrieved
    // above into the local SquareUp database.
    var referenceDataSaveResult =
        await squareReferenceDataService.SaveAsync(
            locations,
            products);

    Console.WriteLine();
    Console.WriteLine("Reference data saved to database:");
    Console.WriteLine(
        $"  Locations: {referenceDataSaveResult.LocationsSaved}");
    Console.WriteLine(
        $"  Catalogue variations: {referenceDataSaveResult.CatalogVariationsSaved}");

    // Populate ProductMapping using Square SKU as the BO product code.
    // The catalogue variation has already been written above.
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

        await squareReferenceDataRepository.SetProductMappingAsync(
            product.VariationId,
            boProductCode,
            product.IsActive);

        productMappingsSaved++;
    }

    Console.WriteLine();
    Console.WriteLine("Product mappings saved to database:");
    Console.WriteLine($"  Saved: {productMappingsSaved}");
    Console.WriteLine($"  Skipped: {productMappingsSkipped}");

    // Extract the Location IDs required by the Orders API.
    var locationIds = locations
        .Where(location => !string.IsNullOrWhiteSpace(location.Id))
        .Select(location => location.Id!)
        .ToList();

    if (locationIds.Count == 0)
    {
        Console.WriteLine();
        Console.WriteLine(
            "Locations were returned, but none contained a valid Location ID.");

        return;
    }

    // Work out tomorrow's date using UK local time.
    var ukTimeZone = GetUkTimeZone();

    var ukNow = TimeZoneInfo.ConvertTime(
        DateTimeOffset.UtcNow,
        ukTimeZone);

    var collectionDate = DateOnly.FromDateTime(
        ukNow.DateTime.AddDays(1));

    Console.WriteLine();
    Console.WriteLine(
        $"Searching for orders due for collection on {collectionDate:dd/MM/yyyy}...");


    var allOrders =
    await squareOrderService.GetOrdersAsync(locationIds);

    Console.WriteLine();
    Console.WriteLine($"All orders returned by Square: {allOrders.Count}");

    foreach (var order in allOrders)
    {
        Console.WriteLine(
            $"  {order.Id} - Location: {order.LocationId}");
    }

    Console.WriteLine();

    // Filter the orders already returned by Square.
    // This avoids making a second SearchOrders API call and ensures
    // that the collection-date filter works against the same snapshot.
    var ordersForCollection =
        squareOrderService.GetOrdersForCollectionDate(
            allOrders,
            collectionDate);

    Console.WriteLine();
    Console.WriteLine(
        $"Orders for collection on {collectionDate:dd/MM/yyyy}:");

    Console.WriteLine();
    Console.WriteLine(
        $"Filtered orders count: {ordersForCollection.Count}");

    foreach (var order in allOrders)
    {
        Console.WriteLine();
        Console.WriteLine($"Checking Order: {order.Id}");

        if (order.Fulfillments == null)
        {
            Console.WriteLine("  No fulfillments.");
            continue;
        }

        foreach (var fulfillment in order.Fulfillments)
        {
            Console.WriteLine(
                $"  Fulfillment Type: {fulfillment.Type}");

            Console.WriteLine(
                $"  Pickup At: " +
                $"{fulfillment.PickupDetails?.PickupAt ?? "<none>"}");
        }
    }

    if (!ordersForCollection.Any())
    {
        Console.WriteLine("No orders found for collection tomorrow.");
        return;
    }

    if (!ordersForCollection.Any())
    {
        Console.WriteLine("No orders found for collection tomorrow.");
        return;
    }

    // Stage the complete orders due for collection tomorrow.
    // This writes:
    //   Square.SquareOrder
    //   Square.OrderFulfillment
    //   Square.OrderLine
    var orderPersistenceResult =
        await squareOrderPersistenceService.SaveAsync(
            ordersForCollection);

    Console.WriteLine();
    Console.WriteLine("Orders staged to database:");
    Console.WriteLine(
        $"  Orders: {orderPersistenceResult.OrdersSaved}");
    Console.WriteLine(
        $"  Fulfilments: {orderPersistenceResult.FulfillmentsSaved}");
    Console.WriteLine(
        $"  Order lines: {orderPersistenceResult.OrderLinesSaved}");

    foreach (var order in ordersForCollection)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 70));

        Console.WriteLine($"Order ID: {order.Id}");
        Console.WriteLine($"Location ID: {order.LocationId}");
        Console.WriteLine($"Order State: {order.State}");

        if (order.Fulfillments != null)
        {
            foreach (var fulfillment in order.Fulfillments)
            {
                Console.WriteLine();
                Console.WriteLine("Pickup:");
                Console.WriteLine($"  Type: {fulfillment.Type}");
                Console.WriteLine($"  State: {fulfillment.State}");

                if (fulfillment.PickupDetails != null)
                {
                    var pickupAtText =
                        fulfillment.PickupDetails.PickupAt;

                    if (TryConvertToUkTime(
                        pickupAtText,
                        ukTimeZone,
                        out var pickupAtUk))
                    {
                        Console.WriteLine(
                            $"  Pickup At: {pickupAtUk:dd/MM/yyyy HH:mm:ss} UK");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"  Pickup At: {pickupAtText ?? "<not supplied>"}");
                    }

                    var scheduleType =
                        fulfillment.PickupDetails.ScheduleType?.ToString();

                    Console.WriteLine(
                        $"  Schedule Type: " +
                        $"{(string.IsNullOrWhiteSpace(scheduleType) ? "<not supplied>" : scheduleType)}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("Items:");

        if (order.LineItems == null || !order.LineItems.Any())
        {
            Console.WriteLine("  No line items found.");
            continue;
        }

        foreach (var item in order.LineItems)
        {
            Console.WriteLine($"  Name: {item.Name}");
            Console.WriteLine($"  Quantity: {item.Quantity}");

            Console.WriteLine(
                $"  Catalog Object ID: " +
                $"{item.CatalogObjectId ?? "<none - ad hoc>"}");

            if (item.BasePriceMoney?.Amount is long amount)
            {
                Console.WriteLine(
                    $"  Unit Price: " +
                    $"{FormatMoney(
                        amount,
                        item.BasePriceMoney.Currency?.ToString())}");
            }
            else
            {
                Console.WriteLine("  Unit Price: <not supplied>");
            }

            Console.WriteLine();
        }
    }
}
catch (SquareApiException ex)
{
    Console.WriteLine("Square API request failed.");
    Console.WriteLine($"Status code: {ex.StatusCode}");
    Console.WriteLine($"Message: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine("Application error.");
    Console.WriteLine(ex.Message);
}


static bool TryConvertToUkTime(
    string? pickupAtText,
    TimeZoneInfo ukTimeZone,
    out DateTimeOffset pickupAtUk)
{
    pickupAtUk = default;

    if (string.IsNullOrWhiteSpace(pickupAtText))
    {
        return false;
    }

    if (!DateTimeOffset.TryParse(
        pickupAtText,
        CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind,
        out var pickupAt))
    {
        return false;
    }

    pickupAtUk = TimeZoneInfo.ConvertTime(
        pickupAt,
        ukTimeZone);

    return true;
}

static TimeZoneInfo GetUkTimeZone()
{
    try
    {
        // Windows
        return TimeZoneInfo.FindSystemTimeZoneById(
            "GMT Standard Time");
    }
    catch (TimeZoneNotFoundException)
    {
        // Linux / macOS
        return TimeZoneInfo.FindSystemTimeZoneById(
            "Europe/London");
    }
}

static string FormatMoney(
    long amount,
    string? currency)
{
    var majorUnits = amount / 100m;

    if (string.Equals(
        currency,
        "GBP",
        StringComparison.OrdinalIgnoreCase))
    {
        return $"£{majorUnits:N2}";
    }

    return $"{majorUnits:N2} {currency ?? string.Empty}".Trim();
}
