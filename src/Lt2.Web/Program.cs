using Lt2.Web;

var builder = WebApplication.CreateBuilder(args);
builder.AddLt2Web();

var app = builder.Build();
app.UseLt2Web();
await app.RunAsync();

/// <summary>Entry point, visible to the integration tests' <c>WebApplicationFactory</c>.</summary>
public partial class Program;
