using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace GridScout2
{
  internal class Server
  {
    private static string SERVER_URL =>
        // if we are running from the IDE   
        Debugger.IsAttached
            //? "https://ffew.space/gridscout/"
            ? "http://localhost:3000/"
            : "https://ffew.space/gridscout/";

    // Send a report
    public static async Task SendReport(ScoutMessage message)
    {
      var json = JsonConvert.SerializeObject(message);

      using var client = new HttpClient();
      var content = new StringContent(json, Encoding.UTF8, "application/json");
      var response = await client.PostAsync(SERVER_URL + "api/report", content);
      var body = await response.Content.ReadAsStringAsync();
      Console.WriteLine(body);
    }

    // send an error
    public static async Task SendError(Exception error)
    {
      var json = JsonConvert.SerializeObject(error);

      using var client = new HttpClient();
      var content = new StringContent(json, Encoding.UTF8, "application/json");
      var response = await client.PostAsync(SERVER_URL + "api/error", content);
      var body = await response.Content.ReadAsStringAsync();
      Console.WriteLine(body);
    }
  }
}
