namespace TheRockWasteManagement.Models
{
    public class Booking
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; }
    }
}