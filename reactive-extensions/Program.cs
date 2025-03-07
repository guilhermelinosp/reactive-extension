using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Serilog;

var builder = WebApplication.CreateSlimBuilder(args);
 

builder.Services.AddCors(options =>
{
    options.AddPolicy("*", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("*");

app.MapGet("/stock-stream", async context =>
{
    context.Response.Headers.Append("Content-Type", "text/event-stream");
    context.Response.Headers.Append("Cache-Control", "no-cache");
    context.Response.Headers.Append("Connection", "keep-alive");

    var stopwatch = Stopwatch.StartNew();
    var stockStream = GetStockPriceStream();

    var tcs = new TaskCompletionSource();
    var cancellationObservable = CreateCancellationObservable(context.RequestAborted);

    using var subscription = stockStream
        .TakeUntil(cancellationObservable)
        .Subscribe(
            async update =>
            {
                var data = $"data: {update.Symbol}: {update.Price:F2}\n\n";
                await context.Response.WriteAsync(data);
                await context.Response.Body.FlushAsync();
            },
            ex =>
            {
                Log.Error("error: {ObjMessage}", ex.Message);
                tcs.SetException(ex);
            },
            () => tcs.SetResult()
        );

    stopwatch.Stop();

    context.Response.Headers.Append("Server-Timing", $"app;dur={stopwatch.ElapsedMilliseconds}");

    await tcs.Task;
});

app.MapGet("/", () => "Welcome to the Rx ASP.NET Demo!");

await app.RunAsync();

return;

IObservable<(string Symbol, double Price)> GetStockPriceStream()
{
    var symbols = new[] { "XAI", "TSLA", "SPCE" };

    return Observable
        .Interval(TimeSpan.FromSeconds(1))
        .Select(i =>
        {
            var symbol = symbols[i % symbols.Length];
            var price = 100.0 + new Random().NextDouble() * 10;
            return (Symbol: symbol, Price: price);
        })
        .Take(10);
}

IObservable<Unit> CreateCancellationObservable(CancellationToken requestAbortedToken)
{
    return Observable.Create<Unit>(observer =>
    {
        var registration = requestAbortedToken.Register(() =>
        {
            observer.OnNext(Unit.Default);
            observer.OnCompleted();
        });

        return registration;
    });
}