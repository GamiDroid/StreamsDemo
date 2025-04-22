
using Quartz.Core;

using var quartzClient = new QuartzClient("127.0.0.1", 2566);

try
{
    #region Connect
    quartzClient.Connect();

    Console.WriteLine("Succesfully connected to Quartz"); 
    #endregion

    #region Subscribe
    QuartzMessageHeader header = new QuartzMessageHeader(
        Uid: Guid.NewGuid().ToString(),
        Name: "getProduction",
        DataType: "JSON",
        Receiver: "StreamDemo",
        Type: "subscribe"
        );

    var body = new
    {
        pos = 0,
        line = 2,
        subline = 0,
        type = "Line",
        program = "line",
        location = "Line"
    };

    if (await quartzClient.SendAsync(header, body, CancellationToken.None))
    {
        Console.WriteLine("Message sent successfully");
    }
    else
    {
        Console.WriteLine("Failed to send message");
    } 
    #endregion
}
catch (Exception ex)
{
    Console.WriteLine(ex);
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();

quartzClient.Disconnect();