using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace AlertListener
{
    public static class Function1
    {
        private static string _cognitiveServicesKey;
        private static string _redisConnectionString;
        private static string _cosmosDbKey;

        private static readonly IList<VisualFeatureTypes> Features = new List<VisualFeatureTypes>
        {
            VisualFeatureTypes.Tags
        };

        private static async Task<ImageAnalysis> AnalyzeAsync(IComputerVisionClient computerVision, string imageUrl, ILogger log)
        {
            if (!Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
            {
                log.LogError(
                    "Invalid remoteImageUrl: {0}", imageUrl);
                return null;
            }

            ImageAnalysis analysis =
                await computerVision.AnalyzeImageAsync(imageUrl, Features);

            return analysis;
        }

        [FunctionName("createAlert")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")]
            HttpRequestMessage req,
            ILogger log)
        {
            // Required environment variables
            //if (Environment.GetEnvironmentVariable("CognitiveServicesKey") != null &&
            //    Environment.GetEnvironmentVariable("RedisConnectionString") != null &&
            //Environment.GetEnvironmentVariable("CosmosDbKey") != null)
            //{
            //    _cognitiveServicesKey = Environment.GetEnvironmentVariable("CognitiveServicesKey", EnvironmentVariableTarget.Process);
            //    _redisConnectionString = Environment.GetEnvironmentVariable("RedisConnectionString", EnvironmentVariableTarget.Process);
            //    _cosmosDbKey = Environment.GetEnvironmentVariable("CosmosDbKey", EnvironmentVariableTarget.Process);
            //}

            _cognitiveServicesKey = "7b17c215156240d4aaf8625462269fbf";
            _redisConnectionString = "hackteam7.redis.cache.windows.net:6379,password=49FMcedUIRrSmtqE6HICWdkjRbAxWkh0LQGFyo4So5E=,ssl=False,abortConnect=False";
            _cosmosDbKey = "TfdAclFqUhYimGw1qvmDoGKPT3mbe4zXkv72yFjwbJiyHCKz5lij7Ypv2udTgBq5U2bdjyMlO6CsRGqO9kygqA==";

            if (string.IsNullOrEmpty(_cognitiveServicesKey)) throw new NullReferenceException("Cognitive Services api key is missing!");
            if (string.IsNullOrEmpty(_redisConnectionString)) throw new NullReferenceException("Redis Connection String is missing!");

            log.LogInformation("New Event Grid message received.");

            var messages = await req.Content.ReadAsAsync<JArray>();

            // If the request is for subscription validation, send back the validation code.
            if (messages.Count > 0 && string.Equals((string) messages[0]["eventType"],
                                                    "Microsoft.EventGrid.SubscriptionValidationEvent",
                                                    StringComparison.OrdinalIgnoreCase))
            {
                log.LogInformation("Validate request received");
                return req.CreateResponse<object>(new
                {
                    validationResponse = messages[0]["data"]["validationCode"]
                });
            }

            /*
             * Initialise external services
             */
            ComputerVisionClient computerVision = new ComputerVisionClient(
                new ApiKeyServiceClientCredentials(_cognitiveServicesKey))
            {
                Endpoint = "https://westeurope.api.cognitive.microsoft.com"
            };

            var conn = ConnectionMultiplexer.Connect(_redisConnectionString);

            foreach (var jToken in messages)
            {
                var message = (JObject) jToken;

                // Handle one event.
                EventGridEvent eventGridEvent = message.ToObject<EventGridEvent>();
                log.LogInformation($"Subject: {eventGridEvent.Subject}");
                log.LogInformation($"Time: {eventGridEvent.EventTime}");
                log.LogInformation($"Event data: {eventGridEvent.Data}");

                try
                {
                    var alert = JsonConvert.DeserializeObject<Alert>(eventGridEvent.Data.ToString());

                    log.LogInformation("Alert received {0}", JsonConvert.SerializeObject(alert));
                    log.LogInformation("Performing analysis");

                    var imageAnalysis = await AnalyzeAsync(computerVision, alert.Image, log);

                    log.LogInformation("Alert analysis complete {0}", JsonConvert.SerializeObject(imageAnalysis));

                    var personTag = imageAnalysis.Tags.FirstOrDefault(t => t.Name == "person" && t.Confidence >= 0.90);
                    AlertEvent alertEvent;
                    if (personTag == null)
                    {
                        log.LogInformation("Alert raised by a non-person object.");

                        alertEvent = new AlertEvent(
                            alert.DeviceId,
                            alert.Name,
                            "non-person object has been spotted.",
                            alert.Longitude,
                            alert.Latitude,
                            Status.Ok);
                    }
                    else
                    {
                        log.LogInformation("Alert raised by a person object. We are {0} percent sure.", Math.Round(personTag.Confidence * 100, 2));
                        alertEvent = new AlertEvent(
                            alert.DeviceId,
                            alert.Name,
                            "person has been spotted! quick, catch them.",
                            alert.Longitude,
                            alert.Latitude,
                            Status.Error);
                    }

                    var client = new DocumentClient(new Uri("https://alertdb.documents.azure.com:443/"), _cosmosDbKey);
                    await client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri("AlertSQLDb", "AlertsCollection"), alertEvent);

                    await conn.GetDatabase(0)
                              .PublishAsync(new RedisChannel("alerts", RedisChannel.PatternMode.Auto), alertEvent.ToString());

                    log.LogInformation("Alert successfully sent to redis for further processing.");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, ex.Message);
                    return req.CreateErrorResponse(HttpStatusCode.InternalServerError, "Unable to process image url.");
                }
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }
    }

    public class Alert
    {
        [JsonProperty("DeviceId")]
        public long DeviceId { get; set; }

        [JsonProperty("Image")]
        public string Image { get; set; }

        [JsonProperty("Latitude")]
        public double Latitude { get; set; }

        [JsonProperty("Longitude")]
        public double Longitude { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("Text")]
        public string Text { get; set; }
    }

    public class AlertEvent
    {
        private AlertEvent()
        {
        }

        public AlertEvent(long deviceId, string name, string text, double longitude, double latitude, Status status)
        {
            AlertId = Guid.NewGuid();
            DeviceId = deviceId;
            Name = name;
            Text = text;
            Longitude = longitude;
            Latitude = latitude;
            Status = status.ToString();
        }

        public Guid AlertId { get; private set; }

        [JsonProperty("deviceId")]
        public long DeviceId { get; private set; }

        [JsonProperty("lat")]
        public double Latitude { get; private set; }

        [JsonProperty("long")]
        public double Longitude { get; private set; }

        [JsonProperty("name")]
        public string Name { get; private set; }

        [JsonProperty("status")]
        public string Status { get; private set; }

        [JsonProperty("text")]
        public string Text { get; private set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public enum Status
    {
        Ok,
        Error
    }
}