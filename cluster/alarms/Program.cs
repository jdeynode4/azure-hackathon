using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

namespace alarms
{
    class IOTSimulator
    {
        private static readonly HttpClient _client = new HttpClient();

        // Event Grid
        private static string _eventTopicEndpoint = null;
        private static string _eventAegSasKey = null;

        // Speed of event publishing, ms between each event
        private static int _eventInterval = 30000;

        // Image location and total number  
        private static string _alarmImageRoot = null;
        private static int _alarmImageNumber = 20;

        // Locations for simulated IOT devices
        private static int _numberDevices = 10;

        private static Alarm[] _devices;



        // Maximum time for the events to be generated (in minutes)
        // 0 equates to no maximum (run forever)
        private static int _maxRunTime = 60;
        private static DateTime _endTime;

        // Hold boundary conditions for longitude and latitude
        // Don't need to calculate more than once
        private static int _integralMaxLat;
        private static int _fractionalMaxLat;
        private static int _integralMinLat;
        private static int _fractionalMinLat; 
        private static int _integralMaxLong;
        private static int _fractionalMaxLong;  
        private static int _integralMinLong;
        private static int _fractionalMinLong;

        // Longitude and Latitude boundaries within which to create event locations
        // Example rectangle that describes the bulk of England without hitting sea
        // Bottom left 51.010299, -3.114624 (Taunton)
        // Bottom right 51.083686, -0.145569 (Mid Sussex)
        // Top left 53.810382, -3.048706 (Blackpool)
        // Top right 53.745462, -0.346069 (Hull)
        // Use these as default if not supplied in args
        private static decimal _maxLat = 53.810382m;
        private static decimal _minLat = 51.010299m;
        private static decimal _maxLong = -0.145569m;
        private static decimal _minLong = -3.048706m;
        
        // IOTSimulator 
        static void Main(string[] args)
        {
            string usageOutput = "\nUsage\n" +
            "\nRequired environment variables" + 
            "\n------------------------------" + 
            "\n\nAlarmTopic: The Event Grid Topic EndPoint." +
            "\nAlarmKey: The Event Grid Topic key." + 
            "\nAlarmImageRoot: The URL to the source of the alarm images. Each image in the folder must be named photoXX.png where XX = 01,02 etc.." +
            "\n\nOptional environment variables" +
            "\n------------------------------" + 
            "\nAlarmImageNumber: The number of images in the image URL. Minimum of 2, default = 20." + 
            "\n\nAlarmInterval: The ms between alarm events, default = 10000." +
            "\nAlarmNumDevices: The number of alarms, default = 10." +
            "\nAlarmMaxLat AlarmMinLat AlarmMaxLong AlarmMinLong - Describes the area within which random cordinates will be created, default = central England." +
            "\nLatitude and Longitude must all be decimal with 6 significant points and all 4 must be provided." +
            "\nAlarmMaxRunTime: The maximum number of minutes for the events to be generated, zero for no max. Default = 10";
            
            if (args.Length > 0)
            {
                Console.WriteLine(usageOutput);
                return;
            }
            
            // Required environment variables
            if (Environment.GetEnvironmentVariable("AlarmTopic") != null &&
                Environment.GetEnvironmentVariable("AlarmKey") != null &&
                Environment.GetEnvironmentVariable("AlarmImageRoot") != null)
            {
                _eventTopicEndpoint = Environment.GetEnvironmentVariable("AlarmTopic");
                _eventAegSasKey = Environment.GetEnvironmentVariable("AlarmKey");
                _alarmImageRoot = Environment.GetEnvironmentVariable("AlarmImageRoot");
            }
            else
            {
                Console.WriteLine("Missing required environment variable(s).");
                Console.WriteLine(usageOutput);
                return;
            } 

            // Optional environment variables
            try
            {
                // If the interval is supplied, override the default
                if ( Environment.GetEnvironmentVariable("AlarmImageNumber") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmImageNumber"), out _alarmImageNumber);
                }

                // If the interval is supplied, override the default
                if ( Environment.GetEnvironmentVariable("AlarmInterval") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmInterval"), out _eventInterval);
                }
                
                // If the number of devices is supplied, override the default
                if (Environment.GetEnvironmentVariable("AlarmNumDevices") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmNumDevices"), out _numberDevices);
                }

                // If ALL the boundary locations are supplied, override the defaults
                if (Environment.GetEnvironmentVariable("AlarmMaxLat") != null && 
                    Environment.GetEnvironmentVariable("AlarmMinLat") != null && 
                    Environment.GetEnvironmentVariable("AlarmMaxLong") != null && 
                    Environment.GetEnvironmentVariable("AlarmMinLong") != null)
                {
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMaxLat"), out _maxLat);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMinLat"), out _minLat);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMaxLong"), out _maxLong);
                    decimal.TryParse(Environment.GetEnvironmentVariable("AlarmMinLong"), out _minLong);
                }

                // If the maximimum time is supplied, override the default
                if (Environment.GetEnvironmentVariable("AlarmMaxRunTime") != null)
                {
                    int.TryParse(Environment.GetEnvironmentVariable("AlarmMaxRunTime"), out _maxRunTime);
                }
            }
            catch (Exception e) 
            {
                Console.WriteLine("Environment variable error: " + e.Message);
                Console.WriteLine(usageOutput);
                return;
            }
            
            _alarmImageRoot = ValidateURL(_alarmImageRoot);

            Console.Write("Alarm settings: " + "\n Topic EndPoint: " + _eventTopicEndpoint + 
            "\n Topic Key (last chars): " + _eventAegSasKey.Substring(_eventAegSasKey.Length - 4, 4) + 
            "\n Image URL: " + _alarmImageRoot);
            
            Console.Write("\nAlarms will be sent randomly within each " + _eventInterval + " ms.");
            Console.Write("\nThe simulator will stop after " + _maxRunTime + " mins.\n");

            SetLocationBoundaries(_maxLat, _minLat, _maxLong, _minLong);
            SetDevices();
            _endTime = DateTime.Now.AddMinutes(_maxRunTime);
            
            SimulateAlarms().Wait();
        }

        private static void SetDevices()
        {
            // Create a fixed set of devices
            _devices = new Alarm[_numberDevices];

            // Add location into each Alarm
            for (int i = 0; i < _devices.Length; i++)
            {
                _devices[i] = new Alarm();
                _devices[i].DeviceId = i;
                var location = GetAlarmLocation();
                _devices[i].Longitude = location.longitude;
                _devices[i].Latitude = location.latitude;
                _devices[i].Name = "Alarm " + i;
            }
        }

        private static async Task SimulateAlarms()
        {
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _client.DefaultRequestHeaders.Add("aeg-sas-key", _eventAegSasKey);

            Random randomTimer = new Random(Guid.NewGuid().GetHashCode());
                    
            while(true)
            {
                try
                {    
                    // Choose a random device to send an event from
                    Random randomDevice = new Random(Guid.NewGuid().GetHashCode());
                    int deviceId = randomDevice.Next(0, _devices.Length);
                    
                    _devices[deviceId].Image = GetAlarmImage();
                    _devices[deviceId].Text = "Alarm event raised";
                        
                    // Create a new event
                    AlarmEvent alarmEvent = new AlarmEvent {
                        subject = "Alarm", 
                        id = Guid.NewGuid().ToString(),
                        eventType = "recordInserted", 
                        eventTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFK"),
                        data = _devices[deviceId] };

                    // Event Grid data is an array with one element
                    AlarmEvent[] alarmEvents = { alarmEvent };

                    // Post the data
                    HttpResponseMessage response = await _client.PostAsync(_eventTopicEndpoint, new JsonContent(alarmEvents));

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("\n Device " + _devices[deviceId].DeviceId
                        + ". Longitude: " + _devices[deviceId].Longitude 
                        + ". Latitude: " + _devices[deviceId].Latitude
                        + ". Image: " + _devices[deviceId].Image);
                    }
                    else
                    {
                        Console.WriteLine("Post unsuccessful: " + response.StatusCode + " " + response.ReasonPhrase);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("\nError sending alarm:" + e.Message + e.ToString());
                }
                
                // Exit if max time reached
                if (IsMaxTime())
                {
                    Console.WriteLine("Maximum time reached (" + _maxRunTime + " mins), simulator stopping.");
                    Environment.Exit(1);
                }
                // Pause specified interval before the next batch of alarms
                Thread.Sleep(randomTimer.Next(0, _eventInterval));
            }
        }

        private static (decimal longitude, decimal latitude) GetAlarmLocation()
        {
            Random latRandom = new Random(Guid.NewGuid().GetHashCode());
            int latIntegral = latRandom.Next(_integralMinLat, _integralMaxLat + 1);
            int latFractional = latRandom.Next(_fractionalMinLat, _fractionalMaxLat + 1);
            decimal latitude = latIntegral + (latFractional / 1000000m);

            Random longRandom = new Random(Guid.NewGuid().GetHashCode());
            int longIntegral = longRandom.Next(_integralMinLong, _integralMaxLong + 1);
            int longFractional = latRandom.Next(_fractionalMinLong, _fractionalMaxLong + 1);
            decimal longitude = longIntegral + (longFractional / 1000000m);

            return (longitude, latitude);
        }

        private static string GetAlarmImage()
        {
            string alarmImage = null;
            Random random = new Random(Guid.NewGuid().GetHashCode( ));
            
            // Get a random number between 1 and the total number of images
            int value = random.Next(1, _alarmImageNumber);
            
            if (value < 10)
            {
                alarmImage = _alarmImageRoot + "photo0" + value + ".jpg";
            }
            else
            {
                alarmImage = _alarmImageRoot + "photo" + value + ".jpg";
            }
            
            return alarmImage;
        }

        private static void SetLocationBoundaries(decimal maxLat, decimal minLat, decimal maxLong, decimal minLong)
        {
            // Break the coordinates into integral and fractional components
            // So that each part can be randomly created within the right boundaries
            _integralMaxLat = (int) maxLat;
            decimal decFractionalMaxLat = maxLat - _integralMaxLat;
            _fractionalMaxLat = (int) (decFractionalMaxLat * GetMultiplyer(decFractionalMaxLat));
            
            _integralMinLat = (int) minLat;
            decimal decFractionalMinLat = minLat - _integralMinLat;
            _fractionalMinLat = (int) (decFractionalMinLat * GetMultiplyer(decFractionalMinLat));
            
            _integralMaxLong = (int)maxLong;
            decimal decFractionalMaxLong = maxLong - _integralMaxLong;
            _fractionalMaxLong = (int) (decFractionalMaxLong * GetMultiplyer(decFractionalMaxLong));
            
            _integralMinLong = (int)minLong;
            decimal decFractionalMinLong = minLong - _integralMinLong;
            _fractionalMinLong = (int) (decFractionalMinLong * GetMultiplyer(decFractionalMinLong)); 

            FlipIfNegative(ref _fractionalMaxLong, ref _fractionalMinLong);
            FlipIfNegative(ref _fractionalMaxLat, ref _fractionalMinLat); 
        }

        private static int GetMultiplyer(decimal value)
        {
            int factor;
            
            switch (value.ToString().Length)
            {
                case 1:
                    factor = 10;
                    break; 
                case 2:
                    factor = 100;
                    break; 
                case 3:
                    factor = 1000;
                    break;  
                case 4:
                    factor = 10000;
                    break;
                case 5:
                    factor = 100000;
                    break;
                default:
                    factor = 1000000;
                    break;
            }

            return factor;
        }

        private static void FlipIfNegative(ref int max, ref int min)
        {
            // Deal with negative Longitudes and Latitudes, 
            // so that when getting random number the min and max work correctly
            if (max < 0 && min < 0)
            {
                // Swap them
                int tmpMax = max;
                int tmpMin = min;

                max = tmpMin;
                min = tmpMax;
            } 
        }

        private static bool IsMaxTime()
        {
            bool stop = false;

            // If it's zero, never time out
            if (_maxRunTime == 0)
            {
                stop = false;
            }
            else if(DateTime.Compare(DateTime.Now, _endTime) > 0)
            {
                stop = true;
            }
            
            return stop;
        }

        private static string ValidateURL(string UrlToCheck)
        {
            string endsWith = UrlToCheck.Substring(UrlToCheck.Length - 1);
            if (!endsWith.Equals("/"))
            {
                UrlToCheck = UrlToCheck + "/";
            }
            return UrlToCheck;
        }
    }
}
