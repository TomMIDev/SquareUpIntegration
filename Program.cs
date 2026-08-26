using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Square;
using SquareUpIntegration.Services;

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

using var host = builder.Build();
using var scope = host.Services.CreateScope();

var squareClient =
    scope.ServiceProvider.GetRequiredService<SquareClient>();

var squareOrderService =
    scope.ServiceProvider.GetRequiredService<SquareOrderService>();

try
{
    // Retrieve the Square locations available to this Sandbox account.
    var response = await squareClient.Locations.ListAsync();

    if (response.Locations == null || !response.Locations.Any())
    {
        Console.WriteLine(
            "Square connection succeeded, but no locations were returned.");

        return;
    }

    Console.WriteLine("Square connection succeeded.");
    Console.WriteLine();
    Console.WriteLine("Locations:");

    foreach (var location in response.Locations)
    {
        Console.WriteLine(
            $"{location.Name} - Location ID: {location.Id}");
    }

    // Extract the Location IDs required by the Orders API.
    var locationIds = response.Locations
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

    // Retrieve only orders due for collection tomorrow.
    var ordersForCollection =
        await squareOrderService.GetOrdersForCollectionDateAsync(
            locationIds,
            collectionDate);

    Console.WriteLine();
    Console.WriteLine(
        $"Orders for collection on {collectionDate:dd/MM/yyyy}:");

    if (!ordersForCollection.Any())
    {
        Console.WriteLine("No orders found for collection tomorrow.");
        return;
    }

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
