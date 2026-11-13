using System.Text;
using Lt2.SeimasStandIn;

// Stand-in for the Seimas open-data service: same paths and query strings, synthetic canned answers. Local only.
var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var directory = builder.Configuration["StandIn:ResponsesPath"] ?? Path.Combine(AppContext.BaseDirectory, "Responses");
var responses = StandInResponses.Load(directory);

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers[StandInResponses.SyntheticHeader] = "1";
    await next(context);
});
app.MapGet("/", () => Results.Text("Seimo atvirųjų duomenų pakaitalas: visi atsakymai išgalvoti (LT2-SYNTHETIC).\n"));
app.MapGet("/sip/{**service}", (HttpRequest request) =>
{
    var answer = StandInHandler.Answer(new Uri("http://stand-in" + request.Path + request.QueryString), responses);
    return answer.Status == 200
        ? Results.Bytes(answer.Body, "text/xml; charset=utf-8")
        : Results.Text(Encoding.UTF8.GetString(answer.Body), "text/plain; charset=utf-8", statusCode: answer.Status);
});
app.MapGet("/darbotvarke", (HttpRequest request) =>
{
    var answer = StandInHandler.Answer(new Uri("http://stand-in/darbotvarke"), responses);
    return Results.Bytes(answer.Body, answer.Status == 200 ? "text/html; charset=utf-8" : "text/plain; charset=utf-8");
});
await app.RunAsync();
