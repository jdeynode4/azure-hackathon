namespace alarms
{
    public class Alarm
    {
        public int DeviceId {get; set; }
        public decimal Longitude { get; set; }
        public decimal Latitude { get; set; }
        public string Image { get; set; }
        public string Name {get; set;}
        public string Text {get; set;}
    }
}
