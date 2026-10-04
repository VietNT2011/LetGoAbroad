using EnglishHub.Infrastructure;
using EnglishHub.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// VI: Hai host chia sẻ use case và DB. EN: API and worker share application services and the database.
var builder=Host.CreateApplicationBuilder(args);
builder.Services.AddEnglishHub(builder.Configuration);
builder.Services.AddHostedService<MediaWorker>();
await builder.Build().RunAsync();
