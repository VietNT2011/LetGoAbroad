using Microsoft.Extensions.Hosting;
// VI: Worker là process độc lập, dùng chung nghiệp vụ với API. EN: A separate host shares application services with the API.
var builder = Host.CreateApplicationBuilder(args);
await builder.Build().RunAsync();
