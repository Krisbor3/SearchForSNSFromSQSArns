using Amazon;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

class Program
{
    /// <summary>
    /// Run either `dotnet run extract <filter_word> <region>` or `dotnet run collect <region>`
    /// </summary>
    static async Task Main(string[] args)
    {
        await ChooseFunctionality(args);
    }

    static async Task ExtractSqsArns(string filterWord, string regionName)
    {
        var region = RegionEndpoint.GetBySystemName(regionName);
        var outputFile = "filtered_arns.txt";
        var sqsClient = new AmazonSQSClient(region);
        var queueArns = new List<string>();

        var listQueuesResponse = await sqsClient.ListQueuesAsync(new ListQueuesRequest());
        foreach (var queueUrl in listQueuesResponse.QueueUrls)
        {
            var attrsResponse = await sqsClient.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = new List<string> { "QueueArn" }
            });

            var queueName = queueUrl.Split('/').Last();
            if (queueName.Contains(filterWord, StringComparison.OrdinalIgnoreCase))
            {
                if (attrsResponse.Attributes.TryGetValue("QueueArn", out var arn))
                {
                    queueArns.Add(arn);
                }
            }
        }

        await File.WriteAllLinesAsync(outputFile, queueArns);
        Console.WriteLine($"Found {queueArns.Count} queues matching '{filterWord}'. Written to {outputFile}.");
    }

    static async Task CollectSnsSubscriptions(string regionName)
    {
        var inputFile = "filtered_arns.txt";
        var outputFile = "subscriptions.txt";
        if (!File.Exists(inputFile))
        {
            Console.WriteLine($"Input file '{inputFile}' not found. Run extract first.");
            return;
        }

        var sqsArns = new HashSet<string>(File.ReadAllLines(inputFile));
        var region = RegionEndpoint.GetBySystemName(regionName);
        var snsClient = new AmazonSimpleNotificationServiceClient(region);

        var subscriptions = new List<string>();
        string? nextToken = null;

        do
        {
            var request = new ListSubscriptionsRequest { NextToken = nextToken };
            var response = await snsClient.ListSubscriptionsAsync(request);

            foreach (var sub in response.Subscriptions)
            {
                if (sub.Protocol == "sqs" && sqsArns.Contains(sub.Endpoint))
                {
                    subscriptions.Add(sub.SubscriptionArn);
                }
            }

            nextToken = response.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        await File.WriteAllLinesAsync(outputFile, subscriptions);
        Console.WriteLine($"Found {subscriptions.Count} subscriptions. Written to {outputFile}.");
    }

    private static async Task ChooseFunctionality(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run extract <filter_word> <region>");
            Console.WriteLine("  dotnet run collect <region>");
            return;
        }

        var command = args[0].ToLower();

        if (command == "extract")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: dotnet run extract <filter_word> <region>");
                return;
            }
            await ExtractSqsArns(args[1], args[2]);
        }
        else if (command == "collect")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run collect <region>");
                return;
            }
            await CollectSnsSubscriptions(args[1]);
        }
        else
        {
            Console.WriteLine("Unknown command.");
        }
    }
}
