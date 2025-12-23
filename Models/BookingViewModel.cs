public class BookingViewModel
{
    public string BookingId { get; set; }
    public DateTime Date { get; set; }
    public string Address { get; set; }
    public string Status { get; set; }
    public double FinalPrice { get; set; } // Change to double
    public string ServiceType { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;

    public string SpecialRequest { get; set; }
}